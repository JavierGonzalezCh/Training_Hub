using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GSM.API.Auth;
using GSM.Application.Attendance.Contracts;
using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Identity.Contracts;
using GSM.Application.Identity.Public;
using GSM.Application.Members;
using GSM.Application.Members.Contracts;
using GSM.Application.Members.Public;
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
/// M2 — autorizaciones del miembro: textos por gym, aceptación en papel y en la app, revocación, el
/// candado para salud y medidas, y el aviso de recepción. Por HTTP con identidad real (JWT).
/// </summary>
public sealed class MemberConsentTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "OwnerPass_123";
    private const string Data = "data_processing";
    private const string Waiver = "liability_waiver";

    // --- 1. Texto base y versiones nuevas ---

    [Fact]
    public async Task Without_a_published_document_the_base_text_rules_and_a_new_version_makes_old_acceptances_outdated()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(owner, Body("c-1"));

        var docs = (await owner.GetFromJsonAsync<List<ConsentDocumentItem>>("/api/gym/consent-documents"))!;
        Assert.Equal(2, docs.Count);
        Assert.All(docs, d =>
        {
            Assert.True(d.IsDefault);
            Assert.Equal(0, d.Version);
            Assert.False(string.IsNullOrWhiteSpace(d.Text));
        });

        // Un miembro nuevo (o de antes de la v2) no tiene ninguna autorización: "pendiente de autorizar".
        var before = await StatusesAsync(owner, id);
        Assert.All(before, s => Assert.Equal("not_accepted", s.Status));

        await RegisterAsync(owner, id, Data);
        Assert.Equal("accepted", (await StatusesAsync(owner, id)).Single(s => s.Type == Data).Status);

        // El Owner publica su texto: v1. Quien aceptó el texto base queda pendiente de aceptar la vigente.
        var publish = await owner.PutAsJsonAsync(
            "/api/gym/consent-documents", new PublishConsentDocumentRequest(Data, "Texto propio del gimnasio, versión uno."));
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        var published = (await publish.Content.ReadFromJsonAsync<ConsentDocumentItem>())!;
        Assert.Equal(1, published.Version);
        Assert.False(published.IsDefault);

        var after = (await StatusesAsync(owner, id)).Single(s => s.Type == Data);
        Assert.Equal("outdated", after.Status);
        Assert.Equal(1, after.CurrentVersion);
        Assert.Equal(0, after.AcceptedVersion);

        // Y con v2 pasa lo mismo para quien aceptó v1: la aceptación anterior no se anula.
        await RegisterAsync(owner, id, Data);
        Assert.Equal("accepted", (await StatusesAsync(owner, id)).Single(s => s.Type == Data).Status);
        await owner.PutAsJsonAsync(
            "/api/gym/consent-documents", new PublishConsentDocumentRequest(Data, "Texto propio del gimnasio, versión dos."));
        var v2 = (await StatusesAsync(owner, id)).Single(s => s.Type == Data);
        Assert.Equal("outdated", v2.Status);
        Assert.Equal(1, v2.AcceptedVersion);
        Assert.Equal(2, v2.CurrentVersion);

        // Seguir "pendiente" no cierra el candado de datos: la aceptación anterior sigue siendo una aceptación.
        Assert.True(await HasDataConsentAsync(id));

        // La otra autorización no se ve afectada por publicar la de datos.
        Assert.Equal("not_accepted", (await StatusesAsync(owner, id)).Single(s => s.Type == Waiver).Status);
    }

    [Fact]
    public async Task A_too_short_text_is_422_and_an_unknown_type_is_400_or_422()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);

        var tooShort = await owner.PutAsJsonAsync("/api/gym/consent-documents", new PublishConsentDocumentRequest(Data, "corto"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooShort.StatusCode);

        var unknown = await owner.PutAsJsonAsync(
            "/api/gym/consent-documents", new PublishConsentDocumentRequest("otra", "Un texto suficientemente largo."));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unknown.StatusCode);
    }

    // --- 2. Autorización en papel registrada por el staff ---

    [Fact]
    public async Task Staff_registers_a_paper_authorization_and_for_a_minor_it_is_accepted_by_the_guardian()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var staff = await LoggedInAsync(host, w.StaffEmail);
        var adultId = await CreateAsync(staff, Body("paper-1"));
        var minorId = await CreateAsync(staff, MinorBody("paper-kid"));

        await RegisterAsync(staff, adultId, Waiver);
        await RegisterAsync(staff, minorId, Waiver);

        var adult = (await StatusesAsync(staff, adultId)).Single(s => s.Type == Waiver);
        Assert.Equal("accepted", adult.Status);
        Assert.Equal("front_desk", adult.Channel);
        Assert.Equal(w.StaffId, adult.AcceptedByUserId);
        Assert.Equal("Staff Uno", adult.AcceptedByName);
        Assert.False(adult.AcceptedByGuardian);

        var minor = (await StatusesAsync(staff, minorId)).Single(s => s.Type == Waiver);
        Assert.True(minor.AcceptedByGuardian);
        Assert.Equal("front_desk", minor.Channel);

        // Aceptar dos veces la misma versión vigente es un choque de estado.
        var again = await staff.PostAsJsonAsync($"/api/members/{adultId}/consents", new AcceptConsentRequest(Waiver, null));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        // Una versión que ya no es la vigente tampoco se acepta.
        var stale = await staff.PostAsJsonAsync($"/api/members/{adultId}/consents", new AcceptConsentRequest(Data, 7));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    // --- 3. El miembro en la app ---

    [Fact]
    public async Task A_member_accepts_in_the_app_revokes_and_the_data_gate_follows_each_step()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(owner, Body("app-1"));
        await LinkAsync(id, w.MemberUserId);
        var mine = await LoggedInAsync(host, w.MemberEmail);

        Assert.False(await HasDataConsentAsync(id));

        var pending = (await mine.GetFromJsonAsync<List<MemberConsentStatus>>("/api/me/consents"))!;
        Assert.All(pending, s => Assert.Equal("not_accepted", s.Status));
        Assert.All(pending, s => Assert.False(string.IsNullOrWhiteSpace(s.CurrentText)));

        var accept = await mine.PostAsJsonAsync("/api/me/consents", new AcceptConsentRequest(Data, null));
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        var accepted = (await accept.Content.ReadFromJsonAsync<List<MemberConsentStatus>>())!.Single(s => s.Type == Data);
        Assert.Equal("accepted", accepted.Status);
        Assert.Equal("app", accepted.Channel);
        Assert.False(accepted.AcceptedByGuardian);
        Assert.True(await HasDataConsentAsync(id));

        // Revoca: el candado se cierra y la ficha lo muestra como revocada.
        var revoke = await mine.PostAsync($"/api/me/consents/{Data}/revoke", null);
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        Assert.False(await HasDataConsentAsync(id));
        var revoked = (await StatusesAsync(owner, id)).Single(s => s.Type == Data);
        Assert.Equal("revoked", revoked.Status);
        Assert.NotNull(revoked.RevokedAt);

        // Revocar otra vez es un choque de estado; la exoneración no se revoca.
        Assert.Equal(HttpStatusCode.Conflict, (await mine.PostAsync($"/api/me/consents/{Data}/revoke", null)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await mine.PostAsync($"/api/me/consents/{Waiver}/revoke", null)).StatusCode);

        // Vuelve a aceptar y el candado se abre de nuevo.
        Assert.Equal(HttpStatusCode.OK, (await mine.PostAsJsonAsync("/api/me/consents", new AcceptConsentRequest(Data, null))).StatusCode);
        Assert.True(await HasDataConsentAsync(id));

        // El staff también puede revocar a pedido del titular.
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync($"/api/members/{id}/consents/{Data}/revoke", null)).StatusCode);
        Assert.False(await HasDataConsentAsync(id));
    }

    [Fact]
    public async Task A_minor_cannot_accept_or_revoke_in_the_app_only_the_guardian_at_the_front_desk()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(owner, MinorBody("app-kid"));
        await LinkAsync(id, w.MemberUserId);
        var mine = await LoggedInAsync(host, w.MemberEmail);

        Assert.Equal(HttpStatusCode.Forbidden, (await mine.PostAsJsonAsync("/api/me/consents", new AcceptConsentRequest(Data, null))).StatusCode);
        Assert.False(await HasDataConsentAsync(id));

        // La acepta el acudiente en recepción y el menor ya no puede revocarla por su cuenta.
        await RegisterAsync(owner, id, Data);
        Assert.True(await HasDataConsentAsync(id));
        Assert.Equal(HttpStatusCode.Forbidden, (await mine.PostAsync($"/api/me/consents/{Data}/revoke", null)).StatusCode);
        Assert.True(await HasDataConsentAsync(id));
    }

    // --- 4. Historial ---

    [Fact]
    public async Task Accepting_and_revoking_leave_rows_in_the_member_history()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(owner, Body("hist-c"));

        await RegisterAsync(owner, id, Data);
        await owner.PostAsync($"/api/members/{id}/consents/{Data}/revoke", null);

        var changes = (await owner.GetFromJsonAsync<PagedResult<MemberChangeItem>>($"/api/members/{id}/changes?pageSize=100"))!.Items
            .Where(c => c.Field == "consent_data_processing").OrderBy(c => c.ChangedAt).ToList();

        Assert.Equal(2, changes.Count);
        Assert.Equal("none", changes[0].OldValue);
        Assert.Equal("accepted:0:front_desk", changes[0].NewValue);
        Assert.Equal("accepted:0:front_desk", changes[1].OldValue);
        Assert.Equal("revoked:0", changes[1].NewValue);
        Assert.All(changes, c => Assert.Equal(w.OwnerId, c.ChangedByUserId));
    }

    // --- 5. Permisos y aislamiento por gym ---

    [Fact]
    public async Task Only_the_owner_edits_the_texts_and_another_gym_sees_neither_texts_nor_acceptances()
    {
        var mine = await SeedAsync();
        var other = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, mine.OwnerEmail);
        var staff = await LoggedInAsync(host, mine.StaffEmail);
        var theirs = await LoggedInAsync(host, other.OwnerEmail);
        var id = await CreateAsync(owner, Body("iso-1"));
        await RegisterAsync(owner, id, Data);

        var publish = new PublishConsentDocumentRequest(Data, "Texto propio del gimnasio, versión uno.");
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync("/api/gym/consent-documents", publish)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/gym/consent-documents")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/gym/consent-documents", publish)).StatusCode);

        // El otro gym sigue con el texto base y no ve las autorizaciones de este miembro.
        var theirDocs = (await theirs.GetFromJsonAsync<List<ConsentDocumentItem>>("/api/gym/consent-documents"))!;
        Assert.All(theirDocs, d => Assert.True(d.IsDefault));
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.GetAsync($"/api/members/{id}/consents")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await theirs.PostAsJsonAsync($"/api/members/{id}/consents", new AcceptConsentRequest(Waiver, null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.PostAsync($"/api/members/{id}/consents/{Data}/revoke", null)).StatusCode);
    }

    // --- 6. Recepción ---

    [Fact]
    public async Task Check_in_tells_the_front_desk_when_the_waiver_is_missing()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var staff = await LoggedInAsync(host, w.StaffEmail);
        var id = await CreateAsync(staff, Body("recep-1"));

        // Sin membresía el staff "deja pasar" con motivo; lo que importa aquí es el aviso.
        var first = await staff.PostAsJsonAsync("/api/attendance/checkin", new CheckInRequest(id, null, "Visita de prueba"));
        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
        Assert.True((await first.Content.ReadFromJsonAsync<CheckInResponse>())!.MissingWaiver);

        // Con la autorización de datos pero sin exoneración, sigue faltando.
        await RegisterAsync(staff, id, Data);
        var second = await staff.PostAsJsonAsync("/api/attendance/checkin", new CheckInRequest(id, null, "Visita de prueba"));
        Assert.True((await second.Content.ReadFromJsonAsync<CheckInResponse>())!.MissingWaiver);

        await RegisterAsync(staff, id, Waiver);
        var third = await staff.PostAsJsonAsync("/api/attendance/checkin", new CheckInRequest(id, null, "Visita de prueba"));
        Assert.False((await third.Content.ReadFromJsonAsync<CheckInResponse>())!.MissingWaiver);
    }

    // --- 7. Contrato público para M3/M4 ---

    [Fact]
    public async Task The_public_reader_reflects_data_and_waiver_acceptance_separately()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(owner, Body("reader-1"));

        Assert.False(await HasDataConsentAsync(id));
        Assert.False(await HasWaiverAsync(id));

        await RegisterAsync(owner, id, Waiver);
        Assert.False(await HasDataConsentAsync(id));
        Assert.True(await HasWaiverAsync(id));

        await RegisterAsync(owner, id, Data);
        Assert.True(await HasDataConsentAsync(id));

        await owner.PostAsync($"/api/members/{id}/consents/{Data}/revoke", null);
        Assert.False(await HasDataConsentAsync(id));
        Assert.True(await HasWaiverAsync(id));
    }

    // --- Infraestructura de la prueba ---

    private async Task<bool> HasDataConsentAsync(Guid memberId)
    {
        using var scope = ReaderScope();
        return await scope.ServiceProvider.GetRequiredService<IMemberReader>().HasDataProcessingConsentAsync(memberId);
    }

    private async Task<bool> HasWaiverAsync(Guid memberId)
    {
        using var scope = ReaderScope();
        return await scope.ServiceProvider.GetRequiredService<IMemberReader>().HasLiabilityWaiverAsync(memberId);
    }

    private IServiceScope ReaderScope()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        return scope;
    }

    private async Task LinkAsync(Guid memberId, Guid userId)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var member = await db.Set<Member>().SingleAsync(m => m.Id == memberId);
        member.LinkUser(userId);
        await db.SaveChangesAsync();
    }

    private static async Task RegisterAsync(HttpClient api, Guid memberId, string type)
    {
        var response = await api.PostAsJsonAsync($"/api/members/{memberId}/consents", new AcceptConsentRequest(type, null));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<List<MemberConsentStatus>> StatusesAsync(HttpClient api, Guid memberId)
        => (await api.GetFromJsonAsync<List<MemberConsentStatus>>($"/api/members/{memberId}/consents"))!;

    private static Dictionary<string, object?> Body(string document) => new()
    {
        ["firstName"] = "Ana",
        ["lastName"] = "Prueba",
        ["documentType"] = "cc",
        ["documentNumber"] = document,
        ["birthDate"] = "1990-05-05",
    };

    private static Dictionary<string, object?> MinorBody(string document)
    {
        var body = Body(document);
        body["birthDate"] = DateTime.UtcNow.AddYears(-10).ToString("yyyy-MM-dd");
        body["guardianName"] = "Pedro Padre";
        body["guardianDocumentType"] = "cc";
        body["guardianDocumentNumber"] = "999";
        body["guardianPhone"] = "3001112233";
        body["guardianRelationship"] = "Padre";
        return body;
    }

    private static async Task<Guid> CreateAsync(HttpClient api, Dictionary<string, object?> body)
    {
        var response = await api.PostAsJsonAsync("/api/members", body);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CreateMemberResponse>())!.Id;
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
        var gym = Gym.Create("Consent Gym", $"consent-{suffix}");
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id));
        await db.SaveChangesAsync();

        var ownerEmail = $"owner-{suffix}@test.local";
        var staffEmail = $"staff-{suffix}@test.local";
        var memberEmail = $"member-{suffix}@test.local";
        var ownerId = await provisioning.CreateGymUserAsync(ownerEmail, "Owner Dueño", Password, UserRole.Owner, gym.Id);
        var staffId = await provisioning.CreateGymUserAsync(staffEmail, "Staff Uno", Password, UserRole.Staff, gym.Id);
        var memberUserId = await provisioning.CreateMemberUserAsync(memberEmail, "Cuenta Miembro", Password, gym.Id);
        gym.AssignOwner(ownerId);
        await db.SaveChangesAsync();

        return new World(gym.Id, ownerId, ownerEmail, staffId, staffEmail, memberUserId, memberEmail);
    }

    private sealed record World(
        Guid GymId, Guid OwnerId, string OwnerEmail, Guid StaffId, string StaffEmail, Guid MemberUserId, string MemberEmail);
}
