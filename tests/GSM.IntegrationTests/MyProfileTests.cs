using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GSM.API.Auth;
using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Identity.Contracts;
using GSM.Application.Identity.Public;
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
/// M5 — "Mi perfil" del miembro: ficha propia sin datos del staff, autoedición de contacto y salud con el
/// candado de la autorización de datos, identidad y correo protegidos, el menor de solo lectura, mediciones
/// propias y foto. Por HTTP con identidad real (JWT). El cambio de contraseña y de correo ya los cubren
/// <c>AccountSecurityTests</c> y <c>EmailChangeTests</c>.
/// </summary>
public sealed class MyProfileTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "OwnerPass_123";
    private const string Data = "data_processing";

    [Fact]
    public async Task The_profile_is_the_members_own_and_carries_no_staff_only_data()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(owner, Body("m5-1", phone: "3001112233"));
        await owner.PostAsJsonAsync($"/api/members/{id}/notes", new AddMemberNoteRequest("Nota interna"));
        await LinkAsync(id, w.MemberUserId);
        var mine = await LoggedInAsync(host, w.MemberEmail);

        var response = await mine.GetAsync("/api/me/profile");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var profile = JsonSerializer.Deserialize<MyProfile>(json, JsonOptions)!;

        Assert.Equal(id, profile.Id);
        Assert.Equal("+573001112233", profile.Phone);
        Assert.False(profile.IsMinor);
        Assert.True(profile.HealthConsentRequired);
        Assert.Null(profile.Health);

        // Nada del staff: ni notas, ni origen, ni etiquetas, ni referente.
        var lower = json.ToLowerInvariant();
        Assert.DoesNotContain("nota interna", lower);
        Assert.DoesNotContain("\"source\"", lower);
        Assert.DoesNotContain("\"tags\"", lower);
        Assert.DoesNotContain("referredby", lower);
    }

    [Fact]
    public async Task Staff_and_unlinked_accounts_cannot_use_the_member_profile()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var staff = await LoggedInAsync(host, w.StaffEmail);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/me/profile")).StatusCode);

        // Cuenta de miembro sin ficha vinculada: 403.
        var mine = await LoggedInAsync(host, w.MemberEmail);
        Assert.Equal(HttpStatusCode.Forbidden, (await mine.GetAsync("/api/me/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await mine.GetAsync("/api/me/measurements")).StatusCode);
    }

    [Fact]
    public async Task Editing_contact_normalizes_the_phone_and_leaves_history_rows_made_by_the_member()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(owner, Body("m5-2"));
        await LinkAsync(id, w.MemberUserId);
        var mine = await LoggedInAsync(host, w.MemberEmail);

        var response = await mine.PutAsJsonAsync("/api/me/profile", Update(phone: "3001112233", city: "Bogotá", emergencyName: "Mamá", emergencyPhone: "3205556677"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = (await response.Content.ReadFromJsonAsync<MyProfile>())!;
        Assert.Equal("+573001112233", profile.Phone);
        Assert.Equal("Bogotá", profile.City);
        Assert.Equal("+573205556677", profile.EmergencyContactPhone);

        var changes = (await owner.GetFromJsonAsync<PagedResult<MemberChangeItem>>($"/api/members/{id}/changes?pageSize=100"))!.Items;
        var phone = Assert.Single(changes, c => c.Field == "phone");
        Assert.Equal("member", phone.ChangedByRole);
        Assert.Equal("app", phone.Source);
        Assert.Equal(w.MemberUserId, phone.ChangedByUserId);
        Assert.Contains(changes, c => c.Field == "emergency_contact_name");

        // Un teléfono ininterpretable es 422.
        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            (await mine.PutAsJsonAsync("/api/me/profile", Update(phone: "abc"))).StatusCode);
    }

    [Fact]
    public async Task The_member_edits_their_own_goal_with_history_and_an_invalid_goal_is_422()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(owner, Body("m5-goal"));
        await LinkAsync(id, w.MemberUserId);
        var mine = await LoggedInAsync(host, w.MemberEmail);

        var response = await mine.PutAsJsonAsync("/api/me/profile", Update(goal: "muscle_gain"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("muscle_gain", (await response.Content.ReadFromJsonAsync<MyProfile>())!.Goal);
        Assert.Equal("muscle_gain", (await owner.GetFromJsonAsync<MemberDetail>($"/api/members/{id}"))!.Goal);

        var changes = (await owner.GetFromJsonAsync<PagedResult<MemberChangeItem>>($"/api/members/{id}/changes?pageSize=100"))!.Items;
        var row = Assert.Single(changes, c => c.Field == "goal");
        Assert.Equal("member", row.ChangedByRole);

        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            (await mine.PutAsJsonAsync("/api/me/profile", Update(goal: "volar"))).StatusCode);

        // Vacío lo quita.
        var cleared = await mine.PutAsJsonAsync("/api/me/profile", Update(goal: ""));
        Assert.Null((await cleared.Content.ReadFromJsonAsync<MyProfile>())!.Goal);
    }

    [Fact]
    public async Task Identity_fields_with_another_value_are_403_and_the_same_value_is_accepted()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(owner, Body("m5-3"));
        await LinkAsync(id, w.MemberUserId);
        var mine = await LoggedInAsync(host, w.MemberEmail);

        Assert.Equal(HttpStatusCode.Forbidden, (await mine.PutAsJsonAsync("/api/me/profile", Update(firstName: "Otro"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await mine.PutAsJsonAsync("/api/me/profile", Update(documentNumber: "999"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await mine.PutAsJsonAsync("/api/me/profile", Update(documentType: "ce"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await mine.PutAsJsonAsync("/api/me/profile", Update(birthDate: "1991-01-01"))).StatusCode);

        // Reenviar la ficha tal cual no es un cambio.
        Assert.Equal(
            HttpStatusCode.OK,
            (await mine.PutAsJsonAsync("/api/me/profile", Update(
                firstName: "Ana", documentType: "cc", documentNumber: "m5-3", birthDate: "1990-05-05"))).StatusCode);

        var after = (await owner.GetFromJsonAsync<MemberDetail>($"/api/members/{id}"))!;
        Assert.Equal("Ana", after.FirstName);
        Assert.Equal("m5-3", after.DocumentNumber);
    }

    [Fact]
    public async Task A_different_email_is_422_because_it_goes_through_verification()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(owner, Body("m5-4", email: w.MemberEmail));
        await LinkAsync(id, w.MemberUserId);
        var mine = await LoggedInAsync(host, w.MemberEmail);

        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            (await mine.PutAsJsonAsync("/api/me/profile", Update(email: "nuevo@test.local"))).StatusCode);

        // Reenviar el mismo correo (aunque cambie la capitalización) no cambia nada.
        Assert.Equal(
            HttpStatusCode.OK,
            (await mine.PutAsJsonAsync("/api/me/profile", Update(email: w.MemberEmail.ToUpperInvariant()))).StatusCode);
    }

    [Fact]
    public async Task Health_needs_the_data_authorization_then_saves_with_sensitive_history_and_is_not_logged_as_a_consultation()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(owner, Body("m5-5"));
        await LinkAsync(id, w.MemberUserId);
        var mine = await LoggedInAsync(host, w.MemberEmail);

        var health = new UpdateMemberHealthRequest("Asma", "Salbutamol", null, null);
        Assert.Equal(HttpStatusCode.Conflict, (await mine.PutAsJsonAsync("/api/me/profile", Update(health: health))).StatusCode);

        // La acepta él mismo en la app.
        var accept = await mine.PostAsJsonAsync("/api/me/consents", new AcceptConsentRequest(Data, null));
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);

        var saved = (await (await mine.PutAsJsonAsync("/api/me/profile", Update(health: health))).Content.ReadFromJsonAsync<MyProfile>())!;
        Assert.False(saved.HealthConsentRequired);
        Assert.True(saved.CanEditHealth);
        Assert.Equal("Asma", saved.Health!.MedicalConditions);

        var read = (await mine.GetFromJsonAsync<MyProfile>("/api/me/profile"))!;
        Assert.Equal("Salbutamol", read.Health!.Medications);

        // Las filas de salud son sensibles y no salen en el log del contenido.
        var changes = (await owner.GetFromJsonAsync<PagedResult<MemberChangeItem>>($"/api/members/{id}/changes?pageSize=100"))!.Items;
        var row = Assert.Single(changes, c => c.Field == "medical_conditions");
        Assert.True(row.IsSensitive);
        Assert.Equal("member", row.ChangedByRole);

        // §10: cuando el propio miembro mira su ficha no se registra la consulta.
        var access = (await owner.GetFromJsonAsync<PagedResult<MemberHealthAccessItem>>($"/api/members/{id}/health-access"))!;
        Assert.Empty(access.Items);

        // Un PUT sin salud no la toca.
        await mine.PutAsJsonAsync("/api/me/profile", Update(city: "Cali"));
        Assert.Equal("Asma", (await mine.GetFromJsonAsync<MyProfile>("/api/me/profile"))!.Health!.MedicalConditions);
    }

    [Fact]
    public async Task Revoking_the_authorization_blocks_the_health_section_again()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(owner, Body("m5-6"));
        await LinkAsync(id, w.MemberUserId);
        var mine = await LoggedInAsync(host, w.MemberEmail);

        await mine.PostAsJsonAsync("/api/me/consents", new AcceptConsentRequest(Data, null));
        await mine.PutAsJsonAsync("/api/me/profile", Update(health: new UpdateMemberHealthRequest("Asma", null, null, null)));
        await mine.PostAsync($"/api/me/consents/{Data}/revoke", null);

        var blocked = (await mine.GetFromJsonAsync<MyProfile>("/api/me/profile"))!;
        Assert.True(blocked.HealthConsentRequired);
        Assert.Null(blocked.Health);
        Assert.False(blocked.CanEditHealth);
        Assert.True((await mine.GetFromJsonAsync<MemberMeasurementsView>("/api/me/measurements"))!.ConsentRequired);
    }

    [Fact]
    public async Task A_minor_edits_contact_but_not_health_or_measurements()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(owner, MinorBody("m5-7"));
        await LinkAsync(id, w.MemberUserId);
        // El acudiente acepta la autorización en recepción.
        await owner.PostAsJsonAsync($"/api/members/{id}/consents", new AcceptConsentRequest(Data, null));
        var mine = await LoggedInAsync(host, w.MemberEmail);

        var profile = (await mine.GetFromJsonAsync<MyProfile>("/api/me/profile"))!;
        Assert.True(profile.IsMinor);
        Assert.False(profile.HealthConsentRequired);
        Assert.False(profile.CanEditHealth);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await mine.PutAsJsonAsync("/api/me/profile", Update(health: new UpdateMemberHealthRequest("Asma", null, null, null)))).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await mine.PostAsJsonAsync("/api/me/measurements", new AddMemberMeasurementRequest(null, 50m, 150m))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await mine.PutAsJsonAsync("/api/me/profile", Update(city: "Cali"))).StatusCode);

        // Lo que registró el staff sí lo ve.
        await owner.PostAsJsonAsync($"/api/members/{id}/measurements", new AddMemberMeasurementRequest(null, 50m, 150m));
        Assert.Single((await mine.GetFromJsonAsync<MemberMeasurementsView>("/api/me/measurements"))!.Items);
    }

    [Fact]
    public async Task Own_measurements_are_blocked_without_authorization_then_saved_as_member_with_bmi()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(owner, Body("m5-8"));
        await LinkAsync(id, w.MemberUserId);
        var mine = await LoggedInAsync(host, w.MemberEmail);

        var locked = (await mine.GetFromJsonAsync<MemberMeasurementsView>("/api/me/measurements"))!;
        Assert.True(locked.ConsentRequired);
        Assert.Empty(locked.Items);
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await mine.PostAsJsonAsync("/api/me/measurements", new AddMemberMeasurementRequest(null, 80m, 180m))).StatusCode);

        await mine.PostAsJsonAsync("/api/me/consents", new AcceptConsentRequest(Data, null));

        var first = await mine.PostAsJsonAsync("/api/me/measurements", new AddMemberMeasurementRequest(null, 80m, 180m));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var item = (await first.Content.ReadFromJsonAsync<MemberMeasurementItem>())!;
        Assert.Equal("member", item.RecordedByRole);
        Assert.Equal(w.MemberUserId, item.RecordedByUserId);
        Assert.Equal(24.7m, item.Bmi);

        // Rangos y fecha futura: 422. Peso no positivo: 400.
        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            (await mine.PostAsJsonAsync("/api/me/measurements", new AddMemberMeasurementRequest(null, 10m, null))).StatusCode);
        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            (await mine.PostAsJsonAsync("/api/me/measurements", new AddMemberMeasurementRequest(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)), 70m, null))).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await mine.PostAsJsonAsync("/api/me/measurements", new AddMemberMeasurementRequest(null, 0m, null))).StatusCode);

        // La ve el staff con permiso, con quién la registró.
        var staffView = (await owner.GetFromJsonAsync<MemberMeasurementsView>($"/api/members/{id}/measurements"))!;
        Assert.Equal("member", Assert.Single(staffView.Items).RecordedByRole);
    }

    [Fact]
    public async Task Each_member_only_touches_their_own_profile()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var mineId = await CreateAsync(owner, Body("m5-9a"));
        var otherId = await CreateAsync(owner, Body("m5-9b"));
        await LinkAsync(mineId, w.MemberUserId);
        var mine = await LoggedInAsync(host, w.MemberEmail);

        await mine.PutAsJsonAsync("/api/me/profile", Update(city: "Medellín"));

        Assert.Equal("Medellín", (await owner.GetFromJsonAsync<MemberDetail>($"/api/members/{mineId}"))!.City);
        Assert.Null((await owner.GetFromJsonAsync<MemberDetail>($"/api/members/{otherId}"))!.City);
    }

    [Fact]
    public async Task The_photo_upload_replaces_the_previous_one_and_leaves_history()
    {
        var w = await SeedAsync();
        var storage = new FakeStorage();
        await using var host = RealIdentityHost(storage);
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(owner, Body("m5-10"));
        await LinkAsync(id, w.MemberUserId);
        var mine = await LoggedInAsync(host, w.MemberEmail);

        var first = (await (await mine.PostAsync("/api/me/photo", Png())).Content.ReadFromJsonAsync<MyProfile>())!;
        Assert.StartsWith("https://files.test/member-photos/", first.PhotoUrl);

        var second = (await (await mine.PostAsync("/api/me/photo", Png())).Content.ReadFromJsonAsync<MyProfile>())!;
        Assert.NotEqual(first.PhotoUrl, second.PhotoUrl);
        Assert.Equal([first.PhotoUrl], storage.Deleted);

        var gif = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]) { Headers = { ContentType = new MediaTypeHeaderValue("image/gif") } }, "file", "x.gif" } };
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await mine.PostAsync("/api/me/photo", gif)).StatusCode);

        var changes = (await owner.GetFromJsonAsync<PagedResult<MemberChangeItem>>($"/api/members/{id}/changes?pageSize=100"))!.Items;
        Assert.Equal(2, changes.Count(c => c.Field == "photo_url"));
    }

    // --- Infraestructura de la prueba ---

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static MultipartFormDataContent Png()
        => new() { { new ByteArrayContent([1, 2, 3]) { Headers = { ContentType = new MediaTypeHeaderValue("image/png") } }, "file", "a.png" } };

    private static Dictionary<string, object?> Update(
        string? phone = null,
        string? city = null,
        string? emergencyName = null,
        string? emergencyPhone = null,
        UpdateMemberHealthRequest? health = null,
        string? firstName = null,
        string? documentType = null,
        string? documentNumber = null,
        string? birthDate = null,
        string? email = null,
        string? goal = null) => new()
        {
            ["goal"] = goal,
            ["phone"] = phone,
            ["city"] = city,
            ["emergencyContactName"] = emergencyName,
            ["emergencyContactPhone"] = emergencyPhone,
            ["health"] = health,
            ["firstName"] = firstName,
            ["documentType"] = documentType,
            ["documentNumber"] = documentNumber,
            ["birthDate"] = birthDate,
            ["email"] = email,
        };

    private static Dictionary<string, object?> Body(string document, string? phone = null, string? email = null) => new()
    {
        ["firstName"] = "Ana",
        ["lastName"] = "Perfil",
        ["documentType"] = "cc",
        ["documentNumber"] = document,
        ["birthDate"] = "1990-05-05",
        ["phone"] = phone,
        ["email"] = email,
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

    private async Task LinkAsync(Guid memberId, Guid userId)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var member = await db.Set<Member>().SingleAsync(m => m.Id == memberId);
        member.LinkUser(userId);
        await db.SaveChangesAsync();
    }

    private WebApplicationFactory<Program> RealIdentityHost(IFileStorage? storage = null)
        => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUserService>();
            services.AddSingleton<ICurrentUserService, CurrentUser>();
            if (storage is not null)
            {
                services.RemoveAll<IFileStorage>();
                services.AddSingleton(storage);
            }
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
        var ownerId = await provisioning.CreateGymUserAsync(ownerEmail, "Owner Dueño", Password, UserRole.Owner, gym.Id);
        var staffId = await provisioning.CreateGymUserAsync(staffEmail, "Staff Uno", Password, UserRole.Staff, gym.Id);
        var memberUserId = await provisioning.CreateMemberUserAsync(memberEmail, "Cuenta Miembro", Password, gym.Id);
        gym.AssignOwner(ownerId);
        await db.SaveChangesAsync();

        return new World(gym.Id, ownerId, ownerEmail, staffId, staffEmail, memberUserId, memberEmail);
    }

    private sealed record World(
        Guid GymId, Guid OwnerId, string OwnerEmail, Guid StaffId, string StaffEmail, Guid MemberUserId, string MemberEmail);

    private sealed class FakeStorage : IFileStorage
    {
        public List<string> Deleted { get; } = [];

        public Task<string> SaveAsync(FileToStore file, string folder, CancellationToken cancellationToken = default)
            => Task.FromResult($"https://files.test/{folder}/{Guid.NewGuid():N}.png");

        public Task DeleteAsync(string publicUrl, CancellationToken cancellationToken = default)
        {
            Deleted.Add(publicUrl);
            return Task.CompletedTask;
        }
    }
}
