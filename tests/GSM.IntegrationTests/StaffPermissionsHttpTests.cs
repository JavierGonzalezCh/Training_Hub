using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GSM.API.Auth;
using GSM.Application.Common.Abstractions;
using GSM.Application.Identity;
using GSM.Application.Identity.Abstractions;
using GSM.Application.Identity.Contracts;
using GSM.Application.Identity.Public;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>Endpoint de prueba protegido por la política de un permiso especial (solo existe en los tests).</summary>
[ApiController]
[Route("test-probe")]
public sealed class PermissionProbeController : ControllerBase
{
    [HttpGet("finances")]
    [RequirePermission(StaffPermission.ViewFinances)]
    public IActionResult Finances() => Ok("finanzas");

    [HttpGet("trainer")]
    [RequirePermission(StaffPermission.Trainer)]
    public IActionResult Trainer() => Ok("entrenador");
}

/// <summary>
/// I3: perfiles y permisos de staff de punta a punta, con identidad real (JWT): el claim <c>perm</c>,
/// el cierre de sesiones al quitar, la política por permiso y la auditoría.
/// </summary>
public sealed class StaffPermissionsHttpTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "OwnerPass_123";

    [Fact]
    public async Task Existing_staff_migrates_to_Administrator_with_its_four_permissions_in_the_token()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();

        // Simula la fila de antes de I3 (sin perfil) y corre el script de la migración.
        using (var scope = factory.CreateScopeWithMigration())
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE identity.gym_accesses SET profile = NULL WHERE user_id = {0}", w.StaffId);
            await db.Database.ExecuteSqlRawAsync(GSM.Infrastructure.Migrations.MigrationScripts.Read("I3"));
        }

        var staff = await LoginAsync(host, w.StaffEmail, Password);

        Assert.Equal(
            new[] { "manage_attendance", "view_finances", "view_member_health", "void_payments" },
            PermClaims(staff.Tokens!.AccessToken).Order().ToArray());
    }

    [Fact]
    public async Task Inviting_a_Trainer_gives_a_token_with_Trainer_and_ViewMemberHealth_only()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail, Password);
        var emails = host.Services.GetRequiredService<CapturingEmailSender>();
        var email = $"coach-{Guid.NewGuid():N}"[..14] + "@test.local";

        var created = await owner.PostAsJsonAsync(
            "/api/gym/staff", new CreateStaffRequest(email, "Carlos Coach", "trainer"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var staff = (await created.Content.ReadFromJsonAsync<StaffResponse>())!;
        Assert.Equal("trainer", staff.Profile);
        Assert.False(staff.IsAdjusted);

        var link = EmailLinks.Parse(emails.LastTo(email)!);
        (await host.CreateClient().PostAsJsonAsync("/api/auth/activate",
            new ActivateAccountRequest(link.Email, link.Code!, "CoachPass_12345"))).EnsureSuccessStatusCode();

        var login = await LoginAsync(host, email, "CoachPass_12345");
        Assert.Equal(
            new[] { "trainer", "view_member_health" }, PermClaims(login.Tokens!.AccessToken).Order().ToArray());
    }

    [Fact]
    public async Task Inviting_without_a_profile_defaults_to_Reception_and_an_unknown_profile_is_a_400()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail, Password);

        var plain = await owner.PostAsJsonAsync(
            "/api/gym/staff", new CreateStaffRequest($"rec-{Guid.NewGuid():N}"[..14] + "@test.local", "Rita"));
        var reception = (await plain.Content.ReadFromJsonAsync<StaffResponse>())!;
        Assert.Equal("reception", reception.Profile);
        Assert.Empty(reception.Permissions);

        var bogus = await owner.PostAsJsonAsync(
            "/api/gym/staff", new CreateStaffRequest($"bad-{Guid.NewGuid():N}"[..14] + "@test.local", "X", "dios"));
        Assert.Equal(HttpStatusCode.BadRequest, bogus.StatusCode);
    }

    [Fact]
    public async Task Adjusting_adds_and_removes_single_permissions_over_the_profile()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail, Password);

        // Entrenador + ManageAttendance.
        var plus = await PutPermissionsAsync(owner, w.StaffId, "trainer", "trainer", "view_member_health", "manage_attendance");
        Assert.Equal("trainer", plus.Profile);
        Assert.True(plus.IsAdjusted);
        Assert.Equal(
            new[] { "manage_attendance", "trainer", "view_member_health" }, plus.Permissions.Order().ToArray());

        var staff = await LoginAsync(host, w.StaffEmail, Password);
        Assert.Equal(
            new[] { "manage_attendance", "trainer", "view_member_health" },
            PermClaims(staff.Tokens!.AccessToken).Order().ToArray());

        // Le quita ViewMemberHealth: ya no está.
        var minus = await PutPermissionsAsync(owner, w.StaffId, "trainer", "trainer", "manage_attendance");
        Assert.Equal(new[] { "manage_attendance", "trainer" }, minus.Permissions.Order().ToArray());
        var again = await LoginAsync(host, w.StaffEmail, Password);
        Assert.DoesNotContain("view_member_health", PermClaims(again.Tokens!.AccessToken));

        // Volver a las casillas del perfil deja de ser un ajuste.
        var clean = await PutPermissionsAsync(owner, w.StaffId, "trainer", "trainer", "view_member_health");
        Assert.False(clean.IsAdjusted);

        var listed = (await owner.GetFromJsonAsync<List<StaffResponse>>("/api/gym/staff"))!
            .Single(s => s.Id == w.StaffId);
        Assert.False(listed.IsAdjusted);
        Assert.Equal("trainer", listed.Profile);
    }

    [Fact]
    public async Task Removing_a_permission_revokes_that_access_sessions_but_not_its_other_gym_and_adding_does_not()
    {
        var w = await SeedAsync(sharedStaffInSecondGym: true);
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail, Password);

        // Primero se le agrega todo (Administrador): agregar no cierra sesiones.
        var anonymous = host.CreateClient();
        var login = await anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequest(w.StaffEmail, Password));
        var selection = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.True(selection.RequiresGymSelection);
        var accessA = selection.Accesses!.Single(a => a.GymId == w.GymId);
        var accessB = selection.Accesses!.Single(a => a.GymId == w.OtherGymId);
        var sessionA = await SelectAsync(anonymous, selection.SelectionToken!, accessA.AccessId);
        var sessionB = await SelectAsync(anonymous, selection.SelectionToken!, accessB.AccessId);

        await PutPermissionsAsync(
            owner, w.StaffId, "administrator",
            "view_member_health", "manage_attendance", "void_payments", "view_finances");

        var stillAlive = await anonymous.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(sessionA.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, stillAlive.StatusCode);
        var renewed = (await stillAlive.Content.ReadFromJsonAsync<AuthTokensResponse>())!;
        Assert.Contains("view_finances", PermClaims(renewed.AccessToken));

        // Ahora se le quita ViewFinances: sus sesiones en el gym A se cierran; las del gym B, no.
        await PutPermissionsAsync(
            owner, w.StaffId, "administrator", "view_member_health", "manage_attendance", "void_payments");

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(renewed.RefreshToken))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(sessionA.RefreshToken))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await anonymous.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(sessionB.RefreshToken))).StatusCode);

        // Volver a entrar da un token ya sin el permiso quitado.
        var relogin = await anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequest(w.StaffEmail, Password));
        var again = (await relogin.Content.ReadFromJsonAsync<LoginResponse>())!;
        var newA = await SelectAsync(anonymous, again.SelectionToken!, again.Accesses!.Single(a => a.GymId == w.GymId).AccessId);
        Assert.DoesNotContain("view_finances", PermClaims(newA.AccessToken));
    }

    [Fact]
    public async Task Changing_to_a_profile_with_fewer_permissions_revokes_sessions_too()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail, Password);
        await PutPermissionsAsync(
            owner, w.StaffId, "administrator",
            "view_member_health", "manage_attendance", "void_payments", "view_finances");
        var staff = await LoginAsync(host, w.StaffEmail, Password);

        // Pasa a Recepción: pierde cuatro permisos.
        var response = await owner.PutAsJsonAsync(
            $"/api/gym/staff/{w.StaffId}/permissions", new UpdateStaffPermissionsRequest("reception", []));
        response.EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await host.CreateClient().PostAsJsonAsync(
                "/api/auth/refresh", new RefreshRequest(staff.Tokens!.RefreshToken))).StatusCode);
    }

    [Fact]
    public async Task Owner_has_every_permission_staff_only_theirs_and_an_impersonating_superadmin_passes_any()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();

        var owner = await LoginAsync(host, w.OwnerEmail, Password);
        Assert.Equal(5, PermClaims(owner.Tokens!.AccessToken).Count);
        var ownerClient = ClientWith(host, owner.Tokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await ownerClient.GetAsync("/test-probe/finances")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ownerClient.GetAsync("/test-probe/trainer")).StatusCode);

        // Staff de Recepción: sin permisos especiales → 403.
        var reception = await LoginAsync(host, w.StaffEmail, Password);
        var receptionClient = ClientWith(host, reception.Tokens!.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await receptionClient.GetAsync("/test-probe/finances")).StatusCode);

        // Con el permiso en el token → 200; solo el que tiene.
        var ownerHttp = await LoggedInAsync(host, w.OwnerEmail, Password);
        await PutPermissionsAsync(ownerHttp, w.StaffId, "reception", "view_finances");
        var withFinances = ClientWith(host, (await LoginAsync(host, w.StaffEmail, Password)).Tokens!.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await withFinances.GetAsync("/test-probe/finances")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await withFinances.GetAsync("/test-probe/trainer")).StatusCode);

        // Sin token → 401.
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.CreateClient().GetAsync("/test-probe/finances")).StatusCode);

        // SuperAdmin impersonando: cumple cualquiera. Sin impersonar, no.
        var jwt = host.Services.GetRequiredService<IJwtTokenService>();
        var impersonation = jwt.CreateImpersonationToken(
            new ImpersonationTokenRequest(Guid.CreateVersion7(), "Soporte", w.GymId, Guid.CreateVersion7()));
        var impersonating = ClientWith(host, impersonation.Value);
        Assert.Equal(HttpStatusCode.OK, (await impersonating.GetAsync("/test-probe/finances")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await impersonating.GetAsync("/test-probe/trainer")).StatusCode);
    }

    [Fact]
    public async Task Current_user_reports_permissions_from_the_token()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var ownerHttp = await LoggedInAsync(host, w.OwnerEmail, Password);
        await PutPermissionsAsync(ownerHttp, w.StaffId, "reception", "manage_attendance");
        var staffToken = (await LoginAsync(host, w.StaffEmail, Password)).Tokens!.AccessToken;

        var claims = PermClaims(staffToken)
            .Select(p => new System.Security.Claims.Claim(GsmClaims.Permission, p))
            .Append(new System.Security.Claims.Claim(GsmClaims.Role, "staff"))
            .ToList();
        var principal = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(claims, "test", GsmClaims.Name, GsmClaims.Role));

        Assert.True(PermissionRules.Has(principal, StaffPermission.ManageAttendance));
        Assert.False(PermissionRules.Has(principal, StaffPermission.VoidPayments));

        // El mismo claim en un miembro no cuenta: solo el staff (o el owner) los lleva.
        var member = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(GsmClaims.Permission, "manage_attendance"),
             new System.Security.Claims.Claim(GsmClaims.Role, "member")],
            "test", GsmClaims.Name, GsmClaims.Role));
        Assert.False(PermissionRules.Has(member, StaffPermission.ManageAttendance));
    }

    [Fact]
    public async Task Every_change_leaves_audit_rows_only_the_owner_reads_them_and_a_no_op_leaves_none()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail, Password);
        var staffClient = await LoggedInAsync(host, w.StaffEmail, Password);

        // Invitar a otra persona como Entrenador y luego pasarla a Recepción con un permiso extra.
        var email = $"aud-{Guid.NewGuid():N}"[..14] + "@test.local";
        var created = (await (await owner.PostAsJsonAsync(
            "/api/gym/staff", new CreateStaffRequest(email, "Aura Auditada", "trainer")))
            .Content.ReadFromJsonAsync<StaffResponse>())!;

        await PutPermissionsAsync(owner, created.Id, "reception", "manage_attendance");
        // Sin cambios: no deja filas nuevas.
        await PutPermissionsAsync(owner, created.Id, "reception", "manage_attendance");

        var rows = (await owner.GetFromJsonAsync<List<StaffAuditResponse>>($"/api/gym/staff/audit?userId={created.Id}"))!;

        Assert.All(rows, r => Assert.Equal(w.OwnerId, r.ActorUserId));
        Assert.All(rows, r => Assert.Equal("Aura Auditada", r.TargetName));
        Assert.Contains(rows, r => r.Action == "invited" && r.NewValue == "trainer");
        Assert.Contains(rows, r => r.Action == "profile_changed" && r.OldValue == "trainer" && r.NewValue == "reception");
        Assert.Contains(rows, r => r.Action == "permission_revoked" && r.Permission == "trainer");
        Assert.Contains(rows, r => r.Action == "permission_revoked" && r.Permission == "view_member_health");
        Assert.Contains(rows, r => r.Action == "permission_granted" && r.Permission == "manage_attendance");
        Assert.Equal(5, rows.Count);

        // Todo el gym también la ve, pero otro gym no.
        var all = (await owner.GetFromJsonAsync<List<StaffAuditResponse>>("/api/gym/staff/audit"))!;
        Assert.True(all.Count >= rows.Count);
        Assert.All(all, r => Assert.NotEqual(w.OtherOwnerId, r.ActorUserId));

        // El staff no la lee ni cambia permisos.
        Assert.Equal(HttpStatusCode.Forbidden, (await staffClient.GetAsync("/api/gym/staff/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staffClient.PutAsJsonAsync(
            $"/api/gym/staff/{created.Id}/permissions", new UpdateStaffPermissionsRequest("administrator", []))).StatusCode);
    }

    [Fact]
    public async Task Owner_cannot_change_staff_of_another_gym_or_with_unknown_values()
    {
        var w = await SeedAsync();
        var other = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail, Password);

        Assert.Equal(HttpStatusCode.NotFound, (await owner.PutAsJsonAsync(
            $"/api/gym/staff/{other.StaffId}/permissions",
            new UpdateStaffPermissionsRequest("trainer", ["trainer"]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync(
            $"/api/gym/staff/{w.StaffId}/permissions",
            new UpdateStaffPermissionsRequest("trainer", ["hacer_magia"]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync(
            $"/api/gym/staff/{w.StaffId}/permissions",
            new UpdateStaffPermissionsRequest("jefe", []))).StatusCode);

        var profiles = (await owner.GetFromJsonAsync<List<StaffProfileInfo>>("/api/gym/staff/profiles"))!;
        Assert.Equal(3, profiles.Count);
        Assert.Equal(
            new[] { "trainer", "view_member_health" },
            profiles.Single(p => p.Profile == "trainer").Permissions.Order().ToArray());
    }

    // --- Ayudas ---

    private static async Task<StaffResponse> PutPermissionsAsync(
        HttpClient owner, Guid staffId, string profile, params string[] permissions)
    {
        var response = await owner.PutAsJsonAsync(
            $"/api/gym/staff/{staffId}/permissions", new UpdateStaffPermissionsRequest(profile, permissions));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<StaffResponse>())!;
    }

    private static List<string> PermClaims(string accessToken)
    {
        var payload = accessToken.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
        if (!doc.RootElement.TryGetProperty(GsmClaims.Permission, out var perm))
        {
            return [];
        }

        return perm.ValueKind == JsonValueKind.Array
            ? perm.EnumerateArray().Select(e => e.GetString()!).ToList()
            : [perm.GetString()!];
    }

    private WebApplicationFactory<Program> RealIdentityHost()
        => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUserService>();
            services.AddSingleton<ICurrentUserService, CurrentUser>();
            services.AddControllers().AddApplicationPart(typeof(PermissionProbeController).Assembly);
        }));

    private static HttpClient ClientWith(WebApplicationFactory<Program> host, string accessToken)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private static async Task<LoginResponse> LoginAsync(WebApplicationFactory<Program> host, string email, string password)
    {
        var login = await host.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private static async Task<HttpClient> LoggedInAsync(WebApplicationFactory<Program> host, string email, string password)
        => ClientWith(host, (await LoginAsync(host, email, password)).Tokens!.AccessToken);

    private static async Task<AuthTokensResponse> SelectAsync(HttpClient client, string ticket, Guid accessId)
    {
        var response = await client.PostAsJsonAsync("/api/auth/select-gym", new SelectGymRequest(ticket, accessId));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthTokensResponse>())!;
    }

    private async Task<World> SeedAsync(bool sharedStaffInSecondGym = false)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var provisioning = scope.ServiceProvider.GetRequiredService<IUserProvisioning>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = Gym.Create("Perm Gym", $"perm-{suffix}");
        var other = Gym.Create("Perm Gym B", $"permb-{suffix}");
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id), other, GymFeatureFlags.Default(other.Id));
        await db.SaveChangesAsync();

        var ownerEmail = $"owner-{suffix}@test.local";
        var staffEmail = $"staff-{suffix}@test.local";
        var ownerId = await provisioning.CreateGymUserAsync(ownerEmail, "Perm Owner", Password, UserRole.Owner, gym.Id);
        var staffId = await provisioning.CreateGymUserAsync(staffEmail, "Perm Staff", Password, UserRole.Staff, gym.Id);
        gym.AssignOwner(ownerId);

        var otherOwnerId = await provisioning.CreateGymUserAsync(
            $"ownerb-{suffix}@test.local", "Perm Owner B", Password, UserRole.Owner, other.Id);
        other.AssignOwner(otherOwnerId);

        if (sharedStaffInSecondGym)
        {
            // La misma persona es staff en el otro gym: su cuenta se reutiliza y solo se agrega el acceso.
            await provisioning.CreateGymUserAsync(staffEmail, "Perm Staff", Password, UserRole.Staff, other.Id);
        }

        await db.SaveChangesAsync();
        return new World(gym.Id, other.Id, ownerId, ownerEmail, staffId, staffEmail, otherOwnerId);
    }

    private sealed record World(
        Guid GymId, Guid OtherGymId, Guid OwnerId, string OwnerEmail, Guid StaffId, string StaffEmail, Guid OtherOwnerId);
}
