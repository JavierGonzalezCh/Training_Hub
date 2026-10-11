using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GSM.API.Auth;
using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Identity.Contracts;
using GSM.Application.Identity.Public;
using GSM.Application.Members;
using GSM.Application.Members.Contracts;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// M1 — ficha ampliada, historial de cambios, reactivar y notas internas. Por HTTP y con identidad real
/// (JWT), porque la distinción 400 (forma) / 422 (regla) / 409 (choque) la fija el pipeline de la API.
/// </summary>
public sealed class MemberProfileTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "OwnerPass_123";

    // --- 1. Crear con todos los grupos de campos y leerlo igual ---

    [Fact]
    public async Task Create_with_every_field_group_round_trips_and_stores_the_phone_as_e164()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var api = await LoggedInAsync(host, w.OwnerEmail);

        var referrer = await CreateAsync(api, Body("ref-1"));

        var body = Body("full-1");
        body["documentType"] = "ce";
        body["gender"] = "female";
        body["phone"] = "300 123 4567";
        body["email"] = "ana@test.local";
        body["address"] = "Calle 1 # 2-3";
        body["city"] = "Medellín";
        body["emergencyContactName"] = "María Pérez";
        body["emergencyContactRelationship"] = "Madre";
        body["emergencyContactPhone"] = "(311) 222-3344";
        body["source"] = "walk_in";
        body["referredByMemberId"] = referrer;
        body["goal"] = "weight_loss";
        body["tags"] = new[] { "Zumba", "zumba", "  Mañanas ", "" };
        body["joinedAt"] = "2025-03-01";
        var id = await CreateAsync(api, body);

        var member = (await api.GetFromJsonAsync<MemberDetail>($"/api/members/{id}"))!;

        Assert.Equal("ce", member.DocumentType);
        Assert.Equal("full-1", member.DocumentNumber);
        Assert.Equal("female", member.Gender);
        Assert.Equal("+573001234567", member.Phone);
        Assert.Equal("ana@test.local", member.Email);
        Assert.Equal("Calle 1 # 2-3", member.Address);
        Assert.Equal("Medellín", member.City);
        Assert.Equal("María Pérez", member.EmergencyContactName);
        Assert.Equal("Madre", member.EmergencyContactRelationship);
        Assert.Equal("+573112223344", member.EmergencyContactPhone);
        Assert.Equal("walk_in", member.Source);
        Assert.Equal(referrer, member.ReferredByMemberId);
        Assert.Equal("weight_loss", member.Goal);
        Assert.Equal(["Zumba", "Mañanas"], member.Tags);
        Assert.Equal(new DateOnly(2025, 3, 1), member.JoinedAt);
        Assert.False(member.IsMinor);
        Assert.False(member.IsProfileIncomplete);
    }

    // --- 2. Documento duplicado ---

    [Fact]
    public async Task Duplicate_document_is_409_in_the_same_gym_and_accepted_in_another()
    {
        var mine = await SeedAsync();
        var other = await SeedAsync();
        await using var host = RealIdentityHost();
        var api = await LoggedInAsync(host, mine.OwnerEmail);
        var theirs = await LoggedInAsync(host, other.OwnerEmail);

        await CreateAsync(api, Body("dup-1"));

        Assert.Equal(HttpStatusCode.Conflict, (await api.PostAsJsonAsync("/api/members", Body("dup-1"))).StatusCode);

        // Mismo número con otro tipo de documento sí se admite (el índice es por tipo + número).
        var otherType = Body("dup-1");
        otherType["documentType"] = "ti";
        await CreateAsync(api, otherType);

        // Y en otro gym, el mismo documento no choca.
        await CreateAsync(theirs, Body("dup-1"));
    }

    // --- 3. Fecha de nacimiento obligatoria en fichas nuevas (forma → 400) ---

    [Fact]
    public async Task New_member_without_birth_date_is_400()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var api = await LoggedInAsync(host, w.OwnerEmail);

        var body = Body("nobirth");
        body["birthDate"] = null;

        var response = await api.PostAsJsonAsync("/api/members", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("birthDate", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    // --- 4. Menores ---

    [Fact]
    public async Task Minor_without_guardian_is_422_and_with_guardian_is_created()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var api = await LoggedInAsync(host, w.OwnerEmail);

        var child = Body("kid-1");
        child["birthDate"] = DateTime.UtcNow.AddYears(-10).ToString("yyyy-MM-dd");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await api.PostAsJsonAsync("/api/members", child)).StatusCode);

        // Faltando un solo dato del acudiente sigue siendo 422.
        child["guardianName"] = "Pedro Padre";
        child["guardianDocumentType"] = "cc";
        child["guardianDocumentNumber"] = "999";
        child["guardianPhone"] = "3001112233";
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await api.PostAsJsonAsync("/api/members", child)).StatusCode);

        child["guardianRelationship"] = "Padre";
        var id = await CreateAsync(api, child);

        var member = (await api.GetFromJsonAsync<MemberDetail>($"/api/members/{id}"))!;
        Assert.True(member.IsMinor);
        Assert.Equal("Pedro Padre", member.GuardianName);
        Assert.Equal("+573001112233", member.GuardianPhone);
    }

    [Fact]
    public async Task Editing_an_adult_into_a_minor_birth_date_requires_the_guardian()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var api = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(api, Body("adult-1"));

        var update = Body("adult-1");
        update["birthDate"] = DateTime.UtcNow.AddYears(-12).ToString("yyyy-MM-dd");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await api.PutAsJsonAsync($"/api/members/{id}", update)).StatusCode);
    }

    // --- Reglas de dominio del contacto ---

    [Fact]
    public async Task Uninterpretable_phone_is_422_but_an_untouched_legacy_phone_does_not_block_other_edits()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var api = await LoggedInAsync(host, w.OwnerEmail);

        var bad = Body("phone-bad");
        bad["phone"] = "12345";
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await api.PostAsJsonAsync("/api/members", bad)).StatusCode);

        // Un miembro anterior a la v2 con un teléfono que la migración no pudo interpretar.
        var legacyId = await SeedLegacyMemberAsync(w.GymId, "legacy-1", phone: "12345", birthDate: null);

        var list = (await api.GetFromJsonAsync<PagedResult<MemberListItem>>("/api/members?search=legacy-1"))!;
        Assert.True(list.Items.Single().IsProfileIncomplete);

        var update = Body("legacy-1");
        update["phone"] = "12345";
        update["birthDate"] = "1990-01-01";
        update["city"] = "Cali";
        var response = await api.PutAsJsonAsync($"/api/members/{legacyId}", update);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        // El teléfono sigue siendo no interpretable → la ficha sigue incompleta aunque ya tenga fecha.
        var detail = (await response.Content.ReadFromJsonAsync<MemberDetail>())!;
        Assert.Equal("12345", detail.Phone);
        Assert.True(detail.IsProfileIncomplete);

        // Al corregir el teléfono se completa sola.
        update["phone"] = "3001234567";
        detail = (await (await api.PutAsJsonAsync($"/api/members/{legacyId}", update)).Content.ReadFromJsonAsync<MemberDetail>())!;
        Assert.Equal("+573001234567", detail.Phone);
        Assert.False(detail.IsProfileIncomplete);
    }

    [Fact]
    public async Task Referrer_must_be_a_member_of_the_same_gym()
    {
        var mine = await SeedAsync();
        var other = await SeedAsync();
        await using var host = RealIdentityHost();
        var api = await LoggedInAsync(host, mine.OwnerEmail);
        var theirs = await LoggedInAsync(host, other.OwnerEmail);

        var foreign = await CreateAsync(theirs, Body("ref-foreign"));

        var body = Body("ref-bad");
        body["referredByMemberId"] = foreign;
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await api.PostAsJsonAsync("/api/members", body)).StatusCode);

        body["referredByMemberId"] = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await api.PostAsJsonAsync("/api/members", body)).StatusCode);
    }

    [Fact]
    public async Task Unknown_enum_text_is_400()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var api = await LoggedInAsync(host, w.OwnerEmail);

        var body = Body("enum-bad");
        body["source"] = "billboard";

        Assert.Equal(HttpStatusCode.BadRequest, (await api.PostAsJsonAsync("/api/members", body)).StatusCode);
    }

    // --- 5. Historial de cambios ---

    [Fact]
    public async Task Create_leaves_one_created_row_and_each_changed_field_leaves_its_own()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var api = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(api, Body("hist-1"));

        var afterCreate = await ChangesAsync(api, id);
        var created = Assert.Single(afterCreate.Items);
        Assert.Equal("created", created.Field);
        Assert.Equal("owner", created.ChangedByRole);
        Assert.Equal("panel", created.Source);
        Assert.Equal(w.OwnerId, created.ChangedByUserId);
        Assert.Equal("Staff Owner", created.ChangedByName);
        Assert.False(created.IsSensitive);

        // Un PUT sin cambios no deja nada.
        Assert.True((await api.PutAsJsonAsync($"/api/members/{id}", Body("hist-1"))).IsSuccessStatusCode);
        Assert.Single((await ChangesAsync(api, id)).Items);

        // Dos campos cambiados = dos filas, con valor viejo y nuevo.
        var update = Body("hist-1");
        update["city"] = "Bogotá";
        update["phone"] = "3109998877";
        Assert.True((await api.PutAsJsonAsync($"/api/members/{id}", update)).IsSuccessStatusCode);

        var page = await ChangesAsync(api, id);
        Assert.Equal(3, page.Total);
        var city = Assert.Single(page.Items, c => c.Field == "city");
        Assert.Null(city.OldValue);
        Assert.Equal("Bogotá", city.NewValue);
        var phone = Assert.Single(page.Items, c => c.Field == "phone");
        Assert.Null(phone.OldValue);
        Assert.Equal("+573109998877", phone.NewValue);
        Assert.All(new[] { city, phone }, c =>
        {
            Assert.Equal(w.OwnerId, c.ChangedByUserId);
            Assert.True(c.ChangedAt > DateTimeOffset.UtcNow.AddMinutes(-5));
        });

        // Segundo cambio sobre el mismo campo: el valor viejo es el anterior.
        update["city"] = "Cali";
        await api.PutAsJsonAsync($"/api/members/{id}", update);
        var latest = (await ChangesAsync(api, id)).Items.First(c => c.Field == "city");
        Assert.Equal("Bogotá", latest.OldValue);
        Assert.Equal("Cali", latest.NewValue);
    }

    [Fact]
    public async Task Changes_are_paginated_and_only_for_members_of_the_gym()
    {
        var mine = await SeedAsync();
        var other = await SeedAsync();
        await using var host = RealIdentityHost();
        var api = await LoggedInAsync(host, mine.OwnerEmail);
        var theirs = await LoggedInAsync(host, other.OwnerEmail);
        var id = await CreateAsync(api, Body("page-1"));

        var update = Body("page-1");
        update["city"] = "A";
        update["address"] = "B";
        await api.PutAsJsonAsync($"/api/members/{id}", update);

        var first = (await api.GetFromJsonAsync<PagedResult<MemberChangeItem>>($"/api/members/{id}/changes?page=1&pageSize=2"))!;
        var second = (await api.GetFromJsonAsync<PagedResult<MemberChangeItem>>($"/api/members/{id}/changes?page=2&pageSize=2"))!;
        Assert.Equal(3, first.Total);
        Assert.Equal(2, first.Items.Count);
        Assert.Single(second.Items);
        Assert.Equal("created", second.Items[0].Field);

        Assert.Equal(HttpStatusCode.NotFound, (await theirs.GetAsync($"/api/members/{id}/changes")).StatusCode);
    }

    // --- 6. Reactivar ---

    [Fact]
    public async Task Reactivate_returns_an_inactive_member_to_active_with_history_and_an_active_one_is_409()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var api = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(api, Body("react-1"));

        Assert.Equal(HttpStatusCode.Conflict, (await api.PostAsync($"/api/members/{id}/reactivate", null)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteAsync($"/api/members/{id}")).StatusCode);
        Assert.Equal("Inactive", (await api.GetFromJsonAsync<MemberDetail>($"/api/members/{id}"))!.Status);

        var reactivated = await api.PostAsync($"/api/members/{id}/reactivate", null);
        Assert.Equal(HttpStatusCode.OK, reactivated.StatusCode);
        Assert.Equal("Active", (await reactivated.Content.ReadFromJsonAsync<MemberDetail>())!.Status);

        var status = (await ChangesAsync(api, id)).Items.Where(c => c.Field == "status").ToList();
        Assert.Equal(2, status.Count);
        Assert.Contains(status, c => c is { OldValue: "inactive", NewValue: "active" });
        Assert.Contains(status, c => c is { OldValue: "active", NewValue: "inactive" });

        Assert.Equal(HttpStatusCode.Conflict, (await api.PostAsync($"/api/members/{id}/reactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.PostAsync($"/api/members/{Guid.NewGuid()}/reactivate", null)).StatusCode);
    }

    // --- 8. Filtro por etiqueta ---

    [Fact]
    public async Task List_filters_by_tag()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var api = await LoggedInAsync(host, w.OwnerEmail);

        var a = Body("tag-a");
        a["tags"] = new[] { "VIP", "crossfit" };
        var b = Body("tag-b");
        b["tags"] = new[] { "crossfit" };
        var c = Body("tag-c");
        var idA = await CreateAsync(api, a);
        var idB = await CreateAsync(api, b);
        await CreateAsync(api, c);

        var vip = (await api.GetFromJsonAsync<PagedResult<MemberListItem>>("/api/members?tag=vip"))!;
        Assert.Equal([idA], vip.Items.Select(m => m.Id));
        Assert.Equal(["VIP", "crossfit"], vip.Items[0].Tags);   // se guarda como se escribe; el filtro ignora mayúsculas

        var crossfit = (await api.GetFromJsonAsync<PagedResult<MemberListItem>>("/api/members?tag=crossfit"))!;
        Assert.Equal(new[] { idA, idB }.Order(), crossfit.Items.Select(m => m.Id).Order());

        var none = (await api.GetFromJsonAsync<PagedResult<MemberListItem>>("/api/members?tag=nope"))!;
        Assert.Empty(none.Items);
    }

    // --- Notas internas ---

    [Fact]
    public async Task Notes_are_added_by_staff_with_their_author_and_never_reach_a_member_account()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var staff = await LoggedInAsync(host, w.StaffEmail);
        var memberAccount = await LoggedInAsync(host, w.MemberEmail);
        var id = await CreateAsync(owner, Body("note-1"));

        var added = await staff.PostAsJsonAsync($"/api/members/{id}/notes", new AddMemberNoteRequest("Lesión de rodilla, ojo con sentadillas."));
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        await owner.PostAsJsonAsync($"/api/members/{id}/notes", new AddMemberNoteRequest("Prefiere clases de la tarde."));

        var notes = (await owner.GetFromJsonAsync<List<MemberNoteItem>>($"/api/members/{id}/notes"))!;
        Assert.Equal(2, notes.Count);
        Assert.Equal("Prefiere clases de la tarde.", notes[0].Text);
        Assert.Equal(w.OwnerId, notes[0].AuthorUserId);
        Assert.Equal("Lesión de rodilla, ojo con sentadillas.", notes[1].Text);
        Assert.Equal(w.StaffId, notes[1].AuthorUserId);
        Assert.Equal("Staff Uno", notes[1].AuthorName);

        // Una nota vacía es un error de forma.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await staff.PostAsJsonAsync($"/api/members/{id}/notes", new AddMemberNoteRequest("  "))).StatusCode);

        // El miembro (rol member) no las lee ni las escribe.
        Assert.Equal(HttpStatusCode.Forbidden, (await memberAccount.GetAsync($"/api/members/{id}/notes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await memberAccount.PostAsJsonAsync($"/api/members/{id}/notes", new AddMemberNoteRequest("x"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await memberAccount.GetAsync($"/api/members/{id}/changes")).StatusCode);

        // Notas de otro gym: 404.
        var other = await SeedAsync();
        var theirs = await LoggedInAsync(host, other.OwnerEmail);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.GetAsync($"/api/members/{id}/notes")).StatusCode);
    }

    // --- Lo que ve el propio miembro ---

    [Fact]
    public async Task A_member_reading_their_own_record_does_not_see_source_tags_or_referrer()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);

        var body = Body("self-1");
        body["source"] = "google";
        body["tags"] = new[] { "vip" };
        var id = await CreateAsync(owner, body);

        using (var scope = factory.CreateScopeWithMigration())
        {
            scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            var member = await db.Set<Member>().SingleAsync(m => m.Id == id);
            member.LinkUser(w.MemberUserId);
            await db.SaveChangesAsync();
        }

        var mine = await LoggedInAsync(host, w.MemberEmail);
        var detail = (await mine.GetFromJsonAsync<MemberDetail>($"/api/members/{id}"))!;

        Assert.Null(detail.Source);
        Assert.Empty(detail.Tags);
        Assert.Null(detail.ReferredByMemberId);
        Assert.Equal("self-1", detail.DocumentNumber);

        // El staff sí los ve.
        var full = (await owner.GetFromJsonAsync<MemberDetail>($"/api/members/{id}"))!;
        Assert.Equal("google", full.Source);
        Assert.Equal(["vip"], full.Tags);
    }

    // --- Dominio puro ---

    [Theory]
    [InlineData("3001234567", "+573001234567")]
    [InlineData("300 123 4567", "+573001234567")]
    [InlineData("(300) 123-4567", "+573001234567")]
    [InlineData("573001234567", "+573001234567")]
    [InlineData("+57 300 123 4567", "+573001234567")]
    [InlineData("0057 300 123 4567", "+573001234567")]
    [InlineData("6012345678", "+576012345678")]
    [InlineData("+14155552671", "+14155552671")]
    [InlineData("12345", null)]
    [InlineData("2001234567", null)]
    [InlineData("abc", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Phone_numbers_normalize_to_e164_with_colombia_as_default(string? input, string? expected)
        => Assert.Equal(expected, PhoneNumber.TryNormalize(input));

    [Fact]
    public void A_birth_date_cannot_be_removed_from_a_record_that_has_one()
    {
        var today = new DateOnly(2026, 10, 8);
        var member = Member.Create(Guid.NewGuid(), "Ana", "Uno", "1", today, birthDate: new DateOnly(1990, 1, 1));
        var profile = new MemberProfile("Ana", "Uno", DocumentType.Cc, "1", BirthDate: null);

        Assert.Throws<GSM.Domain.Common.Exceptions.ValidationDomainException>(() => member.UpdateProfile(profile, null, today));
    }

    [Fact]
    public void Turning_eighteen_exactly_today_is_not_a_minor()
    {
        var today = new DateOnly(2026, 10, 8);
        var turnsEighteenToday = Member.Create(Guid.NewGuid(), "Ana", "Uno", "1", today, birthDate: new DateOnly(2008, 10, 8));
        var oneDayShort = Member.Create(
            Guid.NewGuid(),
            new MemberProfile(
                "Beto", "Dos", DocumentType.Ti, "2", new DateOnly(2008, 10, 9),
                GuardianName: "Pedro", GuardianDocumentType: DocumentType.Cc, GuardianDocumentNumber: "9",
                GuardianPhone: "3001112233", GuardianRelationship: "Padre"),
            today,
            today);

        Assert.False(turnsEighteenToday.IsMinor(today));
        Assert.True(oneDayShort.IsMinor(today));
    }

    // --- Infraestructura de la prueba ---

    private static Dictionary<string, object?> Body(string document) => new()
    {
        ["firstName"] = "Ana",
        ["lastName"] = "Prueba",
        ["documentType"] = "cc",
        ["documentNumber"] = document,
        ["birthDate"] = "1990-05-05",
    };

    private static async Task<Guid> CreateAsync(HttpClient api, Dictionary<string, object?> body)
    {
        var response = await api.PostAsJsonAsync("/api/members", body);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CreateMemberResponse>())!.Id;
    }

    private static async Task<PagedResult<MemberChangeItem>> ChangesAsync(HttpClient api, Guid memberId)
        => (await api.GetFromJsonAsync<PagedResult<MemberChangeItem>>($"/api/members/{memberId}/changes?pageSize=100"))!;

    private async Task<Guid> SeedLegacyMemberAsync(Guid gymId, string document, string? phone, DateOnly? birthDate)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        // Lo que dejó la migración: un teléfono que no es E.164 y, a veces, sin fecha de nacimiento.
        var member = Member.Create(gymId, "Antiguo", "Miembro", document, new DateOnly(2025, 1, 1), birthDate: birthDate);
        db.Add(member);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"update members.members set phone = {phone} where id = {member.Id}");
        return member.Id;
    }

    private WebApplicationFactory<Program> RealIdentityHost()
        => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUserService>();
            services.AddSingleton<ICurrentUserService, CurrentUser>();
        }));

    private static async Task<HttpClient> LoggedInAsync(WebApplicationFactory<Program> host, string email)
    {
        var client = host.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        login.EnsureSuccessStatusCode();

        var tokens = await login.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.Tokens!.AccessToken);
        return client;
    }

    private async Task<World> SeedAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var provisioning = scope.ServiceProvider.GetRequiredService<IUserProvisioning>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = Gym.Create("Profile Gym", $"profile-{suffix}");
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id));
        await db.SaveChangesAsync();

        var ownerEmail = $"owner-{suffix}@test.local";
        var staffEmail = $"staff-{suffix}@test.local";
        var memberEmail = $"member-{suffix}@test.local";
        var ownerId = await provisioning.CreateGymUserAsync(ownerEmail, "Staff Owner", Password, UserRole.Owner, gym.Id);
        var staffId = await provisioning.CreateGymUserAsync(staffEmail, "Staff Uno", Password, UserRole.Staff, gym.Id);
        var memberUserId = await provisioning.CreateMemberUserAsync(memberEmail, "Cuenta Miembro", Password, gym.Id);
        gym.AssignOwner(ownerId);
        await db.SaveChangesAsync();

        return new World(gym.Id, ownerId, ownerEmail, staffId, staffEmail, memberUserId, memberEmail);
    }

    private sealed record World(
        Guid GymId, Guid OwnerId, string OwnerEmail, Guid StaffId, string StaffEmail, Guid MemberUserId, string MemberEmail);
}
