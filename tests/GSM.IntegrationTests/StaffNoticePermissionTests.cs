using System.Net.Http.Headers;
using System.Net.Http.Json;
using GSM.API.Auth;
using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Identity.Contracts;
using GSM.Application.Identity.Public;
using GSM.Application.Notifications;
using GSM.Application.Notifications.Contracts;
using GSM.Application.Payments.Contracts;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Notifications;
using GSM.Domain.Payments;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// N1b: los avisos de acción de la campana del staff se restringen por permiso. La solicitud de anulación
/// le llega al Owner, a <c>VoidPayments</c> y a <c>ViewFinances</c>; los avisos sin permiso requerido siguen
/// llegando a todo el staff. Con identidad real (JWT).
/// </summary>
public sealed class StaffNoticePermissionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "OwnerPass_123";

    [Fact]
    public async Task Owner_VoidPayments_and_ViewFinances_see_and_count_the_void_request_notice()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        await GrantAsync(owner, w.VoidId, "void_payments");
        await GrantAsync(owner, w.FinanceId, "view_finances");
        var requester = await LoggedInAsync(host, w.PlainEmail);
        await RequestVoidAsync(requester);

        foreach (var email in new[] { w.OwnerEmail, w.VoidEmail, w.FinanceEmail })
        {
            var client = await LoggedInAsync(host, email);
            var bell = (await client.GetFromJsonAsync<PagedResult<StaffNotificationItem>>("/api/staff/notifications?openOnly=true"))!;
            Assert.Contains(bell.Items, n => n.Type == "VoidRequested");
            var count = (await client.GetFromJsonAsync<StaffNotificationCount>("/api/staff/notifications/open-count"))!;
            Assert.True(count.Count >= 1, email);
        }
    }

    [Fact]
    public async Task Staff_without_those_permissions_neither_sees_nor_counts_the_void_request_notice()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var requester = await LoggedInAsync(host, w.PlainEmail);
        await RequestVoidAsync(requester);

        var bell = (await requester.GetFromJsonAsync<PagedResult<StaffNotificationItem>>("/api/staff/notifications?openOnly=false"))!;
        Assert.DoesNotContain(bell.Items, n => n.Type == "VoidRequested");
        var count = (await requester.GetFromJsonAsync<StaffNotificationCount>("/api/staff/notifications/open-count"))!;
        Assert.Equal(0, count.Count);
    }

    [Fact]
    public async Task Notices_without_a_required_permission_still_reach_all_the_staff()
    {
        var w = await SeedAsync();
        await SeedGymNoticeAsync(w.GymId, requiredPermissions: null);
        await using var host = RealIdentityHost();

        foreach (var email in new[] { w.OwnerEmail, w.PlainEmail })
        {
            var client = await LoggedInAsync(host, email);
            var count = (await client.GetFromJsonAsync<StaffNotificationCount>("/api/staff/notifications/open-count"))!;
            Assert.Equal(1, count.Count);
        }
    }

    [Fact]
    public async Task A_notice_restricted_to_another_permission_hides_from_those_who_lack_it()
    {
        var w = await SeedAsync();
        await SeedGymNoticeAsync(w.GymId, [StaffPermission.ManageAttendance]);
        await using var host = RealIdentityHost();

        var plain = await LoggedInAsync(host, w.PlainEmail);
        Assert.Equal(0, (await plain.GetFromJsonAsync<StaffNotificationCount>("/api/staff/notifications/open-count"))!.Count);
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        Assert.Equal(1, (await owner.GetFromJsonAsync<StaffNotificationCount>("/api/staff/notifications/open-count"))!.Count);
    }

    [Fact]
    public async Task ViewFinances_sees_the_notice_but_cannot_close_it_by_hand_and_cannot_hit_it_by_resolve_all()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        await GrantAsync(owner, w.FinanceId, "view_finances");
        var requester = await LoggedInAsync(host, w.PlainEmail);
        await RequestVoidAsync(requester);

        var finance = await LoggedInAsync(host, w.FinanceEmail);
        var bell = (await finance.GetFromJsonAsync<PagedResult<StaffNotificationItem>>("/api/staff/notifications?openOnly=true"))!;
        var notice = Assert.Single(bell.Items, n => n.Type == "VoidRequested");

        var resolve = await finance.PostAsync($"/api/staff/notifications/{notice.Id}/resolve", null);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, resolve.StatusCode);

        // Sin permisos, el aviso ni siquiera existe para quien intenta atenderlo.
        var plain = await LoggedInAsync(host, w.PlainEmail);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await plain.PostAsync($"/api/staff/notifications/{notice.Id}/resolve", null)).StatusCode);
    }

    [Fact]
    public async Task A_superadmin_impersonating_sees_the_restricted_notice()
    {
        var w = await SeedAsync();
        await SeedGymNoticeAsync(w.GymId, StaffNoticeAudience.VoidRequest);

        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>()
            .ActAsImpersonator(Guid.NewGuid(), w.GymId, Guid.NewGuid());
        var count = await scope.ServiceProvider.GetRequiredService<GetStaffNotificationCountHandler>()
            .HandleAsync(default);
        Assert.Equal(1, count.Count);
    }

    // ---- ayudas ----

    private async Task SeedGymNoticeAsync(Guid gymId, IReadOnlyCollection<StaffPermission>? requiredPermissions)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        db.Add(Notification.CreateGymAction(
            gymId, NotificationType.CheckinRejected, Guid.CreateVersion7(), "Aviso de prueba", "Cuerpo", "/members",
            DateTimeOffset.UtcNow.AddMinutes(-1), requiredPermissions: requiredPermissions));
        await db.SaveChangesAsync();
    }

    private static async Task RequestVoidAsync(HttpClient client)
    {
        var paid = await client.PostAsJsonAsync(
            "/api/payments",
            new RecordPaymentRequest(25_000m, PaymentMethod.Card, "Venta de mostrador", null, null, null));
        Assert.Equal(System.Net.HttpStatusCode.Created, paid.StatusCode);
        var payment = (await paid.Content.ReadFromJsonAsync<PaymentResponse>())!;
        var request = await client.PostAsJsonAsync($"/api/payments/{payment.Id}/void-requests", new RequestVoidRequest("Error"));
        Assert.Equal(System.Net.HttpStatusCode.Created, request.StatusCode);
    }

    private static async Task GrantAsync(HttpClient owner, Guid staffId, params string[] permissions)
    {
        var response = await owner.PutAsJsonAsync(
            $"/api/gym/staff/{staffId}/permissions", new UpdateStaffPermissionsRequest("reception", permissions));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private WebApplicationFactory<Program> RealIdentityHost()
        => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUserService>();
            services.AddSingleton<ICurrentUserService, CurrentUser>();
        }));

    private static async Task<HttpClient> LoggedInAsync(WebApplicationFactory<Program> host, string email)
    {
        var login = await host.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        login.EnsureSuccessStatusCode();
        var tokens = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Tokens!;
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    private async Task<World> SeedAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var provisioning = scope.ServiceProvider.GetRequiredService<IUserProvisioning>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = Gym.Create("Notice Perm Gym", $"noticeperm-{suffix}");
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id));
        await db.SaveChangesAsync();

        var ownerEmail = $"owner-{suffix}@test.local";
        var voidEmail = $"void-{suffix}@test.local";
        var financeEmail = $"fin-{suffix}@test.local";
        var plainEmail = $"plain-{suffix}@test.local";
        var ownerId = await provisioning.CreateGymUserAsync(ownerEmail, "Notice Owner", Password, UserRole.Owner, gym.Id);
        var voidId = await provisioning.CreateGymUserAsync(voidEmail, "Void Staff", Password, UserRole.Staff, gym.Id);
        var financeId = await provisioning.CreateGymUserAsync(financeEmail, "Finance Staff", Password, UserRole.Staff, gym.Id);
        await provisioning.CreateGymUserAsync(plainEmail, "Plain Staff", Password, UserRole.Staff, gym.Id);
        gym.AssignOwner(ownerId);
        await db.SaveChangesAsync();
        return new World(gym.Id, ownerEmail, voidId, voidEmail, financeId, financeEmail, plainEmail);
    }

    private sealed record World(
        Guid GymId, string OwnerEmail, Guid VoidId, string VoidEmail, Guid FinanceId, string FinanceEmail, string PlainEmail);
}
