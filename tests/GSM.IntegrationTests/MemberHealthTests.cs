using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GSM.API.Auth;
using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Identity.Contracts;
using GSM.Application.Identity.Public;
using GSM.Application.Members.Contracts;
using GSM.Application.Members.Public;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// M3 — sección de salud: el permiso <c>ViewMemberHealth</c>, el candado de la autorización de datos, el
/// historial sensible y el registro de consultas (§10). Por HTTP con identidad real (JWT).
/// </summary>
public sealed class MemberHealthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "OwnerPass_123";
    private const string Data = "data_processing";

    // --- 1. El candado de la autorización de datos ---

    [Fact]
    public async Task Without_the_data_authorization_the_section_is_blocked_and_nothing_is_saved_or_logged()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(owner, Body("lock-1"));

        var view = (await owner.GetFromJsonAsync<MemberHealthView>($"/api/members/{id}/health"))!;
        Assert.True(view.ConsentRequired);
        Assert.Null(view.Health);

        var put = await owner.PutAsJsonAsync($"/api/members/{id}/health", Health("Asma"));
        Assert.Equal(HttpStatusCode.Conflict, put.StatusCode);

        var access = (await owner.GetFromJsonAsync<PagedResult<MemberHealthAccessItem>>($"/api/members/{id}/health-access"))!;
        Assert.Equal(0, access.Total);
    }

    [Fact]
    public async Task Revoking_the_authorization_blocks_the_section_again()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(owner, Body("lock-2"));
        await AcceptDataAsync(owner, id);

        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"/api/members/{id}/health", Health("Asma"))).StatusCode);

        await owner.PostAsync($"/api/members/{id}/consents/{Data}/revoke", null);

        Assert.True((await owner.GetFromJsonAsync<MemberHealthView>($"/api/members/{id}/health"))!.ConsentRequired);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PutAsJsonAsync($"/api/members/{id}/health", Health("Otra"))).StatusCode);
    }

    // --- 2. Guardar y leer ---

    [Fact]
    public async Task The_owner_saves_and_reads_health_and_the_sheet_only_carries_the_alert_flag()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(owner, Body("save-1"));
        await AcceptDataAsync(owner, id);

        var clearance = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));
        var put = await owner.PutAsJsonAsync($"/api/members/{id}/health", new UpdateMemberHealthRequest(
            " Asma leve ", "Salbutamol", "Sin saltos", clearance));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var view = (await owner.GetFromJsonAsync<MemberHealthView>($"/api/members/{id}/health"))!;
        Assert.False(view.ConsentRequired);
        Assert.Equal("Asma leve", view.Health!.MedicalConditions);
        Assert.Equal("Salbutamol", view.Health.Medications);
        Assert.Equal("Sin saltos", view.Health.TrainingRestrictions);
        Assert.Equal(clearance, view.Health.MedicalClearanceDate);

        // La ficha solo trae el aviso; el detalle no viaja por ahí.
        var raw = await owner.GetStringAsync($"/api/members/{id}");
        Assert.Contains("\"hasHealthAlerts\":true", raw);
        Assert.DoesNotContain("Asma", raw);
        Assert.DoesNotContain("Salbutamol", raw);
    }

    [Fact]
    public async Task Medications_alone_do_not_raise_the_alert_and_empty_texts_clear_the_data()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(owner, Body("alert-1"));
        await AcceptDataAsync(owner, id);

        await owner.PutAsJsonAsync($"/api/members/{id}/health", new UpdateMemberHealthRequest(null, "Vitamina D", null, null));
        Assert.False((await owner.GetFromJsonAsync<MemberDetail>($"/api/members/{id}"))!.HasHealthAlerts);

        await owner.PutAsJsonAsync($"/api/members/{id}/health", new UpdateMemberHealthRequest(null, "Vitamina D", "Rodilla", null));
        Assert.True((await owner.GetFromJsonAsync<MemberDetail>($"/api/members/{id}"))!.HasHealthAlerts);

        // Texto en blanco = sin dato.
        await owner.PutAsJsonAsync($"/api/members/{id}/health", new UpdateMemberHealthRequest("  ", "", "   ", null));
        var view = (await owner.GetFromJsonAsync<MemberHealthView>($"/api/members/{id}/health"))!;
        Assert.Null(view.Health!.MedicalConditions);
        Assert.Null(view.Health.Medications);
        Assert.Null(view.Health.TrainingRestrictions);
        Assert.False((await owner.GetFromJsonAsync<MemberDetail>($"/api/members/{id}"))!.HasHealthAlerts);
    }

    [Fact]
    public async Task A_future_clearance_date_is_422_and_a_text_over_1000_characters_is_400()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(owner, Body("rules-1"));
        await AcceptDataAsync(owner, id);

        var future = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));
        var late = await owner.PutAsJsonAsync($"/api/members/{id}/health", new UpdateMemberHealthRequest("Asma", null, null, future));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, late.StatusCode);

        var tooLong = await owner.PutAsJsonAsync($"/api/members/{id}/health", Health(new string('a', 1001)));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async Task Editing_the_rest_of_the_sheet_does_not_touch_the_health_data()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(owner, Body("keep-1"));
        await AcceptDataAsync(owner, id);
        await owner.PutAsJsonAsync($"/api/members/{id}/health", Health("Hipertensión"));

        var update = Body("keep-1");
        update["firstName"] = "Ana María";
        var response = await owner.PutAsJsonAsync($"/api/members/{id}", update);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var view = (await owner.GetFromJsonAsync<MemberHealthView>($"/api/members/{id}/health"))!;
        Assert.Equal("Hipertensión", view.Health!.MedicalConditions);
    }

    // --- 3. El permiso ViewMemberHealth ---

    [Fact]
    public async Task Staff_without_the_permission_gets_403_on_the_section_but_sees_the_alert_flag_and_can_filter_by_it()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(owner, Body("perm-1"));
        var otherId = await CreateAsync(owner, Body("perm-2"));
        await AcceptDataAsync(owner, id);
        await owner.PutAsJsonAsync($"/api/members/{id}/health", Health("Epilepsia"));

        await SetPermissionsAsync(owner, w.StaffId); // Recepción: sin permisos especiales
        var staff = await LoggedInAsync(host, w.StaffEmail);

        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync($"/api/members/{id}/health")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync($"/api/members/{id}/health", Health("x"))).StatusCode);

        var detail = (await staff.GetFromJsonAsync<MemberDetail>($"/api/members/{id}"))!;
        Assert.True(detail.HasHealthAlerts);

        var filtered = (await staff.GetFromJsonAsync<PagedResult<MemberListItem>>("/api/members?withHealthAlerts=true"))!;
        Assert.Contains(filtered.Items, m => m.Id == id && m.HasHealthAlerts);
        Assert.DoesNotContain(filtered.Items, m => m.Id == otherId);
    }

    [Fact]
    public async Task Staff_with_the_permission_reads_and_edits_and_loses_access_when_it_is_removed()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(owner, Body("perm-3"));
        await AcceptDataAsync(owner, id);

        await SetPermissionsAsync(owner, w.StaffId, "view_member_health");
        var staff = await LoggedInAsync(host, w.StaffEmail);

        Assert.Equal(HttpStatusCode.OK, (await staff.PutAsJsonAsync($"/api/members/{id}/health", Health("Diabetes"))).StatusCode);
        var view = (await staff.GetFromJsonAsync<MemberHealthView>($"/api/members/{id}/health"))!;
        Assert.Equal("Diabetes", view.Health!.MedicalConditions);

        await SetPermissionsAsync(owner, w.StaffId);
        var again = await LoggedInAsync(host, w.StaffEmail);
        Assert.Equal(HttpStatusCode.Forbidden, (await again.GetAsync($"/api/members/{id}/health")).StatusCode);
    }

    // --- 4. Historial sensible ---

    [Fact]
    public async Task Health_changes_land_in_the_history_as_sensitive_rows_only_for_those_who_hold_the_permission()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(owner, Body("hist-1"));
        await AcceptDataAsync(owner, id);

        await owner.PutAsJsonAsync($"/api/members/{id}/health", Health("Asma"));
        await owner.PutAsJsonAsync($"/api/members/{id}/health", Health("Asma"));    // sin cambios: sin filas
        await owner.PutAsJsonAsync($"/api/members/{id}/health", Health("Asma grave")); // un solo campo

        var rows = (await owner.GetFromJsonAsync<PagedResult<MemberChangeItem>>($"/api/members/{id}/changes?pageSize=100"))!.Items
            .Where(c => c.Field == "medical_conditions").OrderBy(c => c.ChangedAt).ToList();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.True(r.IsSensitive));
        Assert.Null(rows[0].OldValue);
        Assert.Equal("Asma", rows[0].NewValue);
        Assert.Equal("Asma", rows[1].OldValue);
        Assert.Equal("Asma grave", rows[1].NewValue);
        Assert.All(rows, r => Assert.Equal(w.OwnerId, r.ChangedByUserId));

        // Recepción ve el historial, pero no las filas de salud (ni en el total).
        await SetPermissionsAsync(owner, w.StaffId);
        var reception = await LoggedInAsync(host, w.StaffEmail);
        var seen = (await reception.GetFromJsonAsync<PagedResult<MemberChangeItem>>($"/api/members/{id}/changes?pageSize=100"))!;
        Assert.DoesNotContain(seen.Items, c => c.IsSensitive);
        Assert.Contains(seen.Items, c => c.Field == "created");
        Assert.DoesNotContain("Asma", await reception.GetStringAsync($"/api/members/{id}/changes?pageSize=100"));

        // Con el permiso las ve.
        await SetPermissionsAsync(owner, w.StaffId, "view_member_health");
        var trainer = await LoggedInAsync(host, w.StaffEmail);
        var all = (await trainer.GetFromJsonAsync<PagedResult<MemberChangeItem>>($"/api/members/{id}/changes?pageSize=100"))!;
        Assert.Equal(seen.Total + 2, all.Total);
    }

    // --- 5. Registro de consultas (§10) ---

    [Fact]
    public async Task Each_viewer_is_logged_once_per_day_and_only_when_the_detail_is_delivered()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(owner, Body("log-1"));
        await AcceptDataAsync(owner, id);
        await owner.PutAsJsonAsync($"/api/members/{id}/health", Health("Asma"));

        // Guardar no cuenta como consulta.
        Assert.Equal(0, (await AccessAsync(owner, id)).Total);

        await owner.GetAsync($"/api/members/{id}/health");
        await owner.GetAsync($"/api/members/{id}/health");
        await owner.GetAsync($"/api/members/{id}/health");
        var afterOwner = await AccessAsync(owner, id);
        Assert.Equal(1, afterOwner.Total);
        Assert.Equal(w.OwnerId, afterOwner.Items[0].UserId);
        Assert.Equal("owner", afterOwner.Items[0].Role);
        Assert.Equal("Owner Dueño", afterOwner.Items[0].UserName);

        // Recepción sin permiso: 403, no vio nada, no se registra.
        await SetPermissionsAsync(owner, w.StaffId);
        var reception = await LoggedInAsync(host, w.StaffEmail);
        await reception.GetAsync($"/api/members/{id}/health");
        Assert.Equal(1, (await AccessAsync(owner, id)).Total);

        // Con el permiso entra en el registro, una vez por día.
        await SetPermissionsAsync(owner, w.StaffId, "view_member_health");
        var trainer = await LoggedInAsync(host, w.StaffEmail);
        await trainer.GetAsync($"/api/members/{id}/health");
        await trainer.GetAsync($"/api/members/{id}/health");
        var both = await AccessAsync(owner, id);
        Assert.Equal(2, both.Total);
        Assert.Contains(both.Items, a => a.UserId == w.StaffId && a.Role == "staff");
    }

    [Fact]
    public async Task Only_the_owner_can_read_the_access_log()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(owner, Body("log-2"));

        await SetPermissionsAsync(owner, w.StaffId, "view_member_health");
        var staff = await LoggedInAsync(host, w.StaffEmail);

        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync($"/api/members/{id}/health-access")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/members/{id}/health-access")).StatusCode);
    }

    // --- 6. Aislamiento por gym y lectura pública ---

    [Fact]
    public async Task Another_gym_cannot_reach_the_section_of_this_gym()
    {
        var a = await SeedAsync();
        var b = await SeedAsync();
        await using var host = RealIdentityHost();
        var ownerA = await LoggedInAsync(host, a.OwnerEmail);
        var ownerB = await LoggedInAsync(host, b.OwnerEmail);
        var id = await CreateAsync(ownerA, Body("tenant-1"));
        await AcceptDataAsync(ownerA, id);
        await ownerA.PutAsJsonAsync($"/api/members/{id}/health", Health("Asma"));

        Assert.Equal(HttpStatusCode.NotFound, (await ownerB.GetAsync($"/api/members/{id}/health")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ownerB.PutAsJsonAsync($"/api/members/{id}/health", Health("x"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ownerB.GetAsync($"/api/members/{id}/health-access")).StatusCode);
    }

    [Fact]
    public async Task The_public_reader_exposes_only_the_alert_flag()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var id = await CreateAsync(owner, Body("reader-1"));

        Assert.False((await ReadInfoAsync(id))!.HasHealthAlerts);

        await AcceptDataAsync(owner, id);
        await owner.PutAsJsonAsync($"/api/members/{id}/health", Health("Asma"));
        Assert.True((await ReadInfoAsync(id))!.HasHealthAlerts);
    }

    // --- Infraestructura de la prueba ---

    private async Task<MemberInfo?> ReadInfoAsync(Guid memberId)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        return await scope.ServiceProvider.GetRequiredService<IMemberReader>().GetAsync(memberId);
    }

    private static UpdateMemberHealthRequest Health(string conditions)
        => new(conditions, null, null, null);

    private static async Task AcceptDataAsync(HttpClient api, Guid memberId)
    {
        var response = await api.PostAsJsonAsync($"/api/members/{memberId}/consents", new AcceptConsentRequest(Data, null));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<PagedResult<MemberHealthAccessItem>> AccessAsync(HttpClient owner, Guid memberId)
        => (await owner.GetFromJsonAsync<PagedResult<MemberHealthAccessItem>>($"/api/members/{memberId}/health-access"))!;

    private static async Task SetPermissionsAsync(HttpClient owner, Guid staffId, params string[] permissions)
    {
        var profile = permissions.Length == 0 ? "reception" : "trainer";
        var response = await owner.PutAsJsonAsync(
            $"/api/gym/staff/{staffId}/permissions", new UpdateStaffPermissionsRequest(profile, permissions));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

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
        var gym = Gym.Create("Health Gym", $"health-{suffix}");
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id));
        await db.SaveChangesAsync();

        var ownerEmail = $"owner-{suffix}@test.local";
        var staffEmail = $"staff-{suffix}@test.local";
        var ownerId = await provisioning.CreateGymUserAsync(ownerEmail, "Owner Dueño", Password, UserRole.Owner, gym.Id);
        var staffId = await provisioning.CreateGymUserAsync(staffEmail, "Staff Uno", Password, UserRole.Staff, gym.Id);
        gym.AssignOwner(ownerId);
        await db.SaveChangesAsync();

        return new World(gym.Id, ownerId, ownerEmail, staffId, staffEmail);
    }

    private sealed record World(Guid GymId, Guid OwnerId, string OwnerEmail, Guid StaffId, string StaffEmail);
}
