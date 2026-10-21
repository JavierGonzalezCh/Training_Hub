using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GSM.API.Auth;
using GSM.Application.Common.Abstractions;
using GSM.Application.Identity.Contracts;
using GSM.Application.Identity.Public;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.SuperAdmin;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// S6: impersonación con modo (lectura/escritura), en silencio, firmada como "TrainingHub" y auditada por la
/// plataforma. Petición HTTP real con la identidad real (JWT), igual que <see cref="SubscriptionGateHttpTests"/>.
/// </summary>
public sealed class ImpersonationWriteHttpTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "OwnerPass_123";
    private const string AdminPassword = "AdminPass_123";

    [Fact]
    public async Task Opening_requires_a_reason_and_a_valid_mode_and_records_the_mode()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var admin = await LoggedInAsync(host, w.AdminEmail, AdminPassword);

        var noReason = await admin.PostAsJsonAsync($"/api/admin/impersonate/{w.GymId}", new { reason = "  ", mode = "write" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, noReason.StatusCode);

        var badMode = await admin.PostAsJsonAsync($"/api/admin/impersonate/{w.GymId}", new { reason = "Soporte", mode = "root" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badMode.StatusCode);

        var defaults = await OpenAsync(admin, w.GymId, "Soporte por defecto", mode: null);
        Assert.Equal("read", defaults.Mode);

        var write = await OpenAsync(admin, w.GymId, "Corregir un cobro", "write");
        Assert.Equal("write", write.Mode);

        using var scope = factory.CreateScopeWithMigration();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var session = await db.Set<ImpersonationSession>().SingleAsync(s => s.Id == write.SessionId);
        Assert.Equal(ImpersonationMode.Write, session.Mode);
        Assert.Equal("Corregir un cobro", session.Reason);
        Assert.Equal(ImpersonationMode.Read,
            (await db.Set<ImpersonationSession>().SingleAsync(s => s.Id == defaults.SessionId)).Mode);
    }

    [Fact]
    public async Task Read_mode_reads_but_every_write_gets_403()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var read = await ImpersonatingAsync(host, w, "read");

        Assert.Equal(HttpStatusCode.OK, (await read.GetAsync($"/api/members/{w.MemberId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await read.GetAsync("/api/gym/staff")).StatusCode);   // solo-Owner, también en lectura

        var write = await read.PostAsJsonAsync("/api/payments", PaymentBody(w.MemberId));
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.Contains("solo lectura", (await write.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail);
    }

    [Fact]
    public async Task Write_mode_writes_and_passes_permission_and_owner_only_endpoints()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var write = await ImpersonatingAsync(host, w, "write");

        var payment = await write.PostAsJsonAsync("/api/payments", PaymentBody(w.MemberId));
        Assert.Equal(HttpStatusCode.Created, payment.StatusCode);
        var paymentId = (await payment.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // Anular exige el permiso VoidPayments: un SuperAdmin impersonando los cumple todos.
        var voided = await write.PostAsJsonAsync($"/api/payments/{paymentId}/void", new { reason = "Error de digitación" });
        Assert.Equal(HttpStatusCode.Created, voided.StatusCode);

        // Y un endpoint solo-Owner admite la escritura (alta de staff sin cuerpo válido llega a validar, no a 403).
        var staff = await write.PostAsJsonAsync("/api/gym/staff", new { });
        Assert.NotEqual(HttpStatusCode.Forbidden, staff.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, staff.StatusCode);
    }

    [Fact]
    public async Task Impersonation_token_cannot_reach_the_platform_endpoints_but_can_end_itself()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var write = await ImpersonatingAsync(host, w, "write");

        Assert.Equal(HttpStatusCode.Forbidden, (await write.GetAsync("/api/admin/gyms")).StatusCode);
        var suspend = await write.PostAsJsonAsync($"/api/admin/gyms/{w.GymId}/suspend", new { reason = "x" });
        Assert.Equal(HttpStatusCode.Forbidden, suspend.StatusCode);
        Assert.Contains("solo sirve dentro del gimnasio", (await suspend.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail);

        Assert.Equal(HttpStatusCode.NoContent, (await write.PostAsync("/api/admin/impersonate/end", null)).StatusCode);
    }

    [Fact]
    public async Task Writes_are_signed_as_GSM_in_the_member_history()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var write = await ImpersonatingAsync(host, w, "write");

        var update = await write.PutAsJsonAsync($"/api/members/{w.MemberId}", new
        {
            firstName = "Ana",
            lastName = "Visitada",
            documentType = "cc",
            documentNumber = w.MemberDocument,
            phone = "3001112222",
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var owner = await LoggedInAsync(host, w.OwnerEmail, Password);
        var history = await owner.GetFromJsonAsync<JsonElement>($"/api/members/{w.MemberId}/changes");
        var phoneChange = history.GetProperty("items").EnumerateArray()
            .Single(c => c.GetProperty("field").GetString() == "phone");

        Assert.Equal("TrainingHub", phoneChange.GetProperty("changedByName").GetString());
        Assert.Equal("superadmin", phoneChange.GetProperty("changedByRole").GetString());
    }

    [Fact]
    public async Task Every_impersonated_request_is_audited_with_its_route_template_and_staff_requests_are_not()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var read = await ImpersonatingAsync(host, w, "read");

        await read.GetAsync($"/api/members/{w.MemberId}");
        await read.PostAsJsonAsync("/api/payments", PaymentBody(w.MemberId));   // 403, también queda

        // Una petición normal del Owner no deja nada.
        var owner = await LoggedInAsync(host, w.OwnerEmail, Password);
        await owner.GetAsync($"/api/members/{w.MemberId}");
        await owner.GetAsync("/api/gym");

        using var scope = factory.CreateScopeWithMigration();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var actions = await db.Set<ImpersonationAction>().Where(a => a.SessionId == w.SessionId)
            .OrderBy(a => a.OccurredAt).ToListAsync();

        Assert.Equal(2, actions.Count);
        Assert.Equal("GET", actions[0].Method);
        Assert.Equal("api/members/{id:guid}", actions[0].Endpoint);   // plantilla, no el id real
        Assert.Equal(200, actions[0].StatusCode);
        Assert.Equal("Members.Get", actions[0].Summary);
        Assert.Equal("POST", actions[1].Method);
        Assert.Equal("api/payments", actions[1].Endpoint);
        Assert.Equal(403, actions[1].StatusCode);
        Assert.Equal(2, await db.Set<ImpersonationAction>().CountAsync());
    }

    [Fact]
    public async Task The_gym_sees_no_sign_of_the_impersonation()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var before = await CountNotificationsAsync();

        var write = await ImpersonatingAsync(host, w, "write");
        await write.GetAsync($"/api/members/{w.MemberId}");
        await write.GetAsync("/api/gym/staff");

        // Sin avisos para los Owners y nada de impersonación en lo que ellos consultan.
        Assert.Equal(before, await CountNotificationsAsync());
        var owner = await LoggedInAsync(host, w.OwnerEmail, Password);
        var notifications = await owner.GetStringAsync("/api/staff/notifications");
        Assert.DoesNotContain("mpersonat", notifications, StringComparison.OrdinalIgnoreCase);
        var staff = await owner.GetStringAsync("/api/gym/staff");
        Assert.DoesNotContain("mpersonat", staff, StringComparison.OrdinalIgnoreCase);
        var gym = await owner.GetStringAsync("/api/gym");
        Assert.DoesNotContain("mpersonat", gym, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_platform_reads_sessions_and_their_actions_and_the_gym_cannot()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var write = await ImpersonatingAsync(host, w, "write");
        await write.GetAsync($"/api/members/{w.MemberId}");

        var admin = await LoggedInAsync(host, w.AdminEmail, AdminPassword);
        var sessions = await admin.GetFromJsonAsync<JsonElement>("/api/admin/impersonations");
        var item = sessions.EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == w.SessionId);
        Assert.Equal("write", item.GetProperty("mode").GetString());
        Assert.Equal(1, item.GetProperty("actionCount").GetInt32());

        var detail = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/impersonations/{w.SessionId}/actions");
        var action = Assert.Single(detail.EnumerateArray());
        Assert.Equal("GET", action.GetProperty("method").GetString());
        Assert.Equal("api/members/{id:guid}", action.GetProperty("endpoint").GetString());

        var owner = await LoggedInAsync(host, w.OwnerEmail, Password);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await owner.GetAsync($"/api/admin/impersonations/{w.SessionId}/actions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/admin/impersonations")).StatusCode);
    }

    [Fact]
    public async Task An_audit_failure_does_not_stop_the_request()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var read = await ImpersonatingAsync(host, w, "read");

        // Una sesión que no existe en la auditoría: la FK falla al guardar la acción, pero la petición sigue.
        var token = host.Services.GetRequiredService<GSM.Application.Identity.Abstractions.IJwtTokenService>()
            .CreateImpersonationToken(new GSM.Application.Identity.Abstractions.ImpersonationTokenRequest(
                w.AdminId, "Soporte", w.GymId, Guid.CreateVersion7(), CanWrite: false));
        var orphan = host.CreateClient();
        orphan.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);

        Assert.Equal(HttpStatusCode.OK, (await orphan.GetAsync($"/api/members/{w.MemberId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await read.GetAsync("/api/gym")).StatusCode);
    }

    // ---------------------------------------------------------------- helpers

    private static object PaymentBody(Guid memberId) => new
    {
        amount = 50000,
        method = "card", // P3: el efectivo exige caja abierta
        concept = "Cobro de soporte",
        memberId,
    };

    private WebApplicationFactory<Program> RealIdentityHost()
        => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUserService>();
            services.AddSingleton<ICurrentUserService, CurrentUser>();
        }));

    private static async Task<HttpClient> LoggedInAsync(
        WebApplicationFactory<Program> host, string email, string password)
    {
        var client = host.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        login.EnsureSuccessStatusCode();
        var tokens = await login.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.Tokens!.AccessToken);
        return client;
    }

    private sealed record Opened(string Token, Guid SessionId, string Mode);

    private static async Task<Opened> OpenAsync(HttpClient admin, Guid gymId, string reason, string? mode)
    {
        var response = mode is null
            ? await admin.PostAsJsonAsync($"/api/admin/impersonate/{gymId}", new { reason })
            : await admin.PostAsJsonAsync($"/api/admin/impersonate/{gymId}", new { reason, mode });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new Opened(
            json.GetProperty("token").GetString()!,
            json.GetProperty("sessionId").GetGuid(),
            json.GetProperty("mode").GetString()!);
    }

    /// <summary>Abre la sesión de verdad (login del superadmin + <c>POST /api/admin/impersonate</c>) y devuelve su cliente.</summary>
    private async Task<HttpClient> ImpersonatingAsync(WebApplicationFactory<Program> host, World w, string mode)
    {
        var admin = await LoggedInAsync(host, w.AdminEmail, AdminPassword);
        var opened = await OpenAsync(admin, w.GymId, "Soporte " + mode, mode);
        w.SessionId = opened.SessionId;

        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", opened.Token);
        return client;
    }

    private async Task<int> CountNotificationsAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        return await db.Database.SqlQueryRaw<int>("select count(*)::int as \"Value\" from notifications.notifications")
            .SingleAsync();
    }

    private async Task<World> SeedAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        scope.ServiceProvider.GetRequiredService<MutableClock>().Unpin();

        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, members.members, " +
            "payments.payments, notifications.notifications, " +
            "superadmin.gym_subscriptions, superadmin.impersonation_sessions restart identity cascade;");

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var adminEmail = $"sa-{suffix}@test.local";
        var ownerEmail = $"own-{suffix}@test.local";
        var provisioning = scope.ServiceProvider.GetRequiredService<IUserProvisioning>();
        var adminId = await provisioning.CreateSuperAdminAsync(adminEmail, "Super Admin", AdminPassword);

        var gym = Gym.Create("S6 Gym", $"s6-{suffix}");
        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        var document = $"d-{suffix}";
        var member = Member.Create(gym.Id, "Ana", "Visitada", document, new DateOnly(2026, 1, 1));
        db.Add(member);
        await db.SaveChangesAsync();

        var ownerId = await provisioning.CreateGymUserAsync(ownerEmail, "Dueña S6", Password, UserRole.Owner, gym.Id);
        gym.AssignOwner(ownerId);
        await db.SaveChangesAsync();

        return new World(adminId, adminEmail, gym.Id, ownerEmail, member.Id, document);
    }

    private sealed class World(Guid adminId, string adminEmail, Guid gymId, string ownerEmail, Guid memberId, string memberDocument)
    {
        public Guid AdminId { get; } = adminId;
        public string AdminEmail { get; } = adminEmail;
        public Guid GymId { get; } = gymId;
        public string OwnerEmail { get; } = ownerEmail;
        public Guid MemberId { get; } = memberId;
        public string MemberDocument { get; } = memberDocument;
        public Guid SessionId { get; set; }
    }
}
