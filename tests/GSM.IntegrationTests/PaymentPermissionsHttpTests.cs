using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Dashboard;
using GSM.Application.Identity;
using GSM.Application.Identity.Contracts;
using GSM.Application.Identity.Public;
using GSM.Application.Notifications.Contracts;
using GSM.Application.Payments.Contracts;
using GSM.API.Auth;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Payments;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// P2: permisos <c>ViewFinances</c> y <c>VoidPayments</c> aplicados a Payments, y solicitudes de anulación,
/// con identidad real (JWT) de punta a punta.
/// </summary>
public sealed class PaymentPermissionsHttpTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "OwnerPass_123";

    // ---- criterio 1: ViewFinances ----

    [Fact]
    public async Task Staff_without_ViewFinances_cannot_list_payments_or_read_the_daily_close_or_see_revenue()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var staff = await LoggedInAsync(host, w.StaffEmail);

        await RecordAsync(staff, 50_000m);

        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/payments")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/cash-register/daily")).StatusCode);

        // Lo que necesita para atender ("¿ya pagó?") sigue abierto: la lista de un miembro no es 403.
        var ofMember = await staff.GetAsync($"/api/payments?memberId={Guid.NewGuid()}");
        Assert.NotEqual(HttpStatusCode.Forbidden, ofMember.StatusCode);

        var dash = (await staff.GetFromJsonAsync<DashboardResponse>("/api/dashboard"))!;
        Assert.Null(dash.RevenueThisMonth);
        Assert.Null(dash.RevenueLastMonth);

        // El Owner ve todo.
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/payments")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/cash-register/daily")).StatusCode);
        var ownerDash = (await owner.GetFromJsonAsync<DashboardResponse>("/api/dashboard"))!;
        Assert.True(ownerDash.RevenueThisMonth >= 50_000m);
    }

    [Fact]
    public async Task Granting_ViewFinances_opens_the_financial_views()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        await GrantAsync(owner, w.StaffId, "view_finances");
        var staff = await LoggedInAsync(host, w.StaffEmail);

        var list = await staff.GetFromJsonAsync<PagedResult<PaymentResponse>>("/api/payments");
        Assert.NotNull(list);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/cash-register/daily")).StatusCode);
        Assert.NotNull((await staff.GetFromJsonAsync<DashboardResponse>("/api/dashboard"))!.RevenueThisMonth);
    }

    // ---- criterio 2: VoidPayments y solicitud ----

    [Fact]
    public async Task Staff_without_VoidPayments_cannot_void_but_can_request_with_a_mandatory_reason()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var staff = await LoggedInAsync(host, w.StaffEmail);
        var payment = await RecordAsync(staff, 80_000m);

        var direct = await staff.PostAsJsonAsync($"/api/payments/{payment.Id}/void", new VoidPaymentRequest("Error de cobro"));
        Assert.Equal(HttpStatusCode.Forbidden, direct.StatusCode);

        var noReason = await staff.PostAsJsonAsync($"/api/payments/{payment.Id}/void-requests", new RequestVoidRequest("  "));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, noReason.StatusCode);

        var ok = await staff.PostAsJsonAsync($"/api/payments/{payment.Id}/void-requests", new RequestVoidRequest("Cobro duplicado"));
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var created = (await ok.Content.ReadFromJsonAsync<VoidRequestResponse>())!;
        Assert.Equal("pending", created.Status);
        Assert.Equal(payment.Id, created.PaymentId);
        Assert.Equal("Cobro duplicado", created.Reason);

        // El pago se ve "anulación solicitada" y sigue sin anularse.
        var fetched = (await staff.GetFromJsonAsync<PaymentResponse>($"/api/payments/{payment.Id}"))!;
        Assert.True(fetched.HasPendingVoidRequest);
        Assert.False(fetched.IsReversed);

        // Quien no tiene el permiso tampoco ve ni resuelve la bandeja.
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/payments/void-requests")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await staff.PostAsync($"/api/payments/void-requests/{created.Id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await staff.PostAsync($"/api/payments/void-requests/{created.Id}/reject", null)).StatusCode);
    }

    [Fact]
    public async Task Owner_and_staff_with_VoidPayments_can_void_directly()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        await GrantAsync(owner, w.StaffId, "void_payments");
        var staff = await LoggedInAsync(host, w.StaffEmail);

        var p1 = await RecordAsync(staff, 10_000m);
        var p2 = await RecordAsync(owner, 20_000m);

        Assert.Equal(HttpStatusCode.Created,
            (await staff.PostAsJsonAsync($"/api/payments/{p1.Id}/void", new VoidPaymentRequest("Error de cobro"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await owner.PostAsJsonAsync($"/api/payments/{p2.Id}/void", new VoidPaymentRequest("Error de cobro"))).StatusCode);
    }

    // ---- criterio 3: aprobar / rechazar ----

    [Fact]
    public async Task Approving_a_request_creates_the_same_inverse_entry_and_cannot_be_resolved_twice()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var staff = await LoggedInAsync(host, w.StaffEmail);
        var payment = await RecordAsync(staff, 120_000m);
        var request = await RequestAsync(staff, payment.Id, "Monto mal digitado");

        var approve = await owner.PostAsync($"/api/payments/void-requests/{request.Id}/approve", null);
        Assert.Equal(HttpStatusCode.Created, approve.StatusCode);
        var reversal = (await approve.Content.ReadFromJsonAsync<PaymentResponse>())!;
        Assert.Equal(-120_000m, reversal.Amount);
        Assert.Equal(payment.Id, reversal.ReversesPaymentId);
        Assert.Equal("Monto mal digitado", reversal.Notes);
        Assert.Equal(payment.Method, reversal.Method);
        Assert.Equal(w.OwnerId, reversal.RegisteredBy);

        var original = (await owner.GetFromJsonAsync<PaymentResponse>($"/api/payments/{payment.Id}"))!;
        Assert.True(original.IsReversed);
        Assert.False(original.HasPendingVoidRequest);

        // Ni aprobar ni rechazar de nuevo: ya está resuelta.
        Assert.Equal(HttpStatusCode.Conflict,
            (await owner.PostAsync($"/api/payments/void-requests/{request.Id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await owner.PostAsync($"/api/payments/void-requests/{request.Id}/reject", null)).StatusCode);

        var resolved = (await owner.GetFromJsonAsync<PagedResult<VoidRequestResponse>>("/api/payments/void-requests?status=approved"))!;
        var row = Assert.Single(resolved.Items, r => r.Id == request.Id);
        Assert.Equal(w.OwnerId, row.ResolvedBy);
        Assert.Equal("Monto mal digitado", row.Reason);
    }

    [Fact]
    public async Task Rejecting_a_request_closes_it_without_touching_the_payment()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var staff = await LoggedInAsync(host, w.StaffEmail);
        var payment = await RecordAsync(staff, 45_000m);
        var request = await RequestAsync(staff, payment.Id, "Cliente se arrepintió");

        Assert.Equal(HttpStatusCode.NoContent,
            (await owner.PostAsync($"/api/payments/void-requests/{request.Id}/reject", null)).StatusCode);

        var original = (await owner.GetFromJsonAsync<PaymentResponse>($"/api/payments/{payment.Id}"))!;
        Assert.False(original.IsReversed);
        Assert.False(original.HasPendingVoidRequest);

        Assert.Equal(HttpStatusCode.Conflict,
            (await owner.PostAsync($"/api/payments/void-requests/{request.Id}/approve", null)).StatusCode);
        var all = (await owner.GetFromJsonAsync<PagedResult<VoidRequestResponse>>("/api/payments/void-requests?status=rejected"))!;
        Assert.Contains(all.Items, r => r.Id == request.Id && r.Status == "rejected");

        // Rechazada, el pago admite una solicitud nueva.
        Assert.Equal(HttpStatusCode.Created,
            (await staff.PostAsJsonAsync($"/api/payments/{payment.Id}/void-requests", new RequestVoidRequest("Otra vez"))).StatusCode);
    }

    [Fact]
    public async Task Rejecting_with_a_note_keeps_it_and_tells_the_requester_why()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var staff = await LoggedInAsync(host, w.StaffEmail);
        var payment = await RecordAsync(staff, 80_000m);
        var request = await RequestAsync(staff, payment.Id, "Cobré de más");

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsJsonAsync(
            $"/api/payments/void-requests/{request.Id}/reject", new RejectVoidRequestRequest("  Pagó dos meses, está bien  "))).StatusCode);

        var rejected = (await owner.GetFromJsonAsync<PagedResult<VoidRequestResponse>>("/api/payments/void-requests?status=rejected"))!;
        Assert.Equal("Pagó dos meses, está bien", Assert.Single(rejected.Items, r => r.Id == request.Id).RejectionNote);

        // Quien pidió no ve la bandeja de solicitudes: le llega un aviso personal con la nota.
        var bell = (await staff.GetFromJsonAsync<PagedResult<StaffNotificationItem>>("/api/staff/notifications?openOnly=false"))!;
        var notice = Assert.Single(bell.Items, n => n.Type == "VoidRequestRejected");
        Assert.Contains("Pagó dos meses, está bien", notice.Body);

        // Nota demasiado larga: 422, y la solicitud sigue pendiente.
        var other = await RequestAsync(staff, (await RecordAsync(staff, 10_000m)).Id, "Otra");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.PostAsJsonAsync(
            $"/api/payments/void-requests/{other.Id}/reject", new RejectVoidRequestRequest(new string('x', 501)))).StatusCode);
        var pending = (await owner.GetFromJsonAsync<PagedResult<VoidRequestResponse>>("/api/payments/void-requests?status=pending"))!;
        Assert.Contains(pending.Items, r => r.Id == other.Id);
    }

    // ---- criterio 4: una pendiente por pago ----

    [Fact]
    public async Task A_payment_with_a_pending_request_does_not_admit_another_and_a_voided_one_neither()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var staff = await LoggedInAsync(host, w.StaffEmail);
        var payment = await RecordAsync(staff, 60_000m);
        await RequestAsync(staff, payment.Id, "Primera");

        Assert.Equal(HttpStatusCode.Conflict,
            (await staff.PostAsJsonAsync($"/api/payments/{payment.Id}/void-requests", new RequestVoidRequest("Segunda"))).StatusCode);

        // Anular directo cierra la pendiente; y un pago ya anulado no admite solicitudes.
        var pendingList = (await owner.GetFromJsonAsync<PagedResult<VoidRequestResponse>>("/api/payments/void-requests?status=pending"))!;
        var pending = Assert.Single(pendingList.Items, r => r.PaymentId == payment.Id);
        Assert.Equal(HttpStatusCode.Created,
            (await owner.PostAsJsonAsync($"/api/payments/{payment.Id}/void", new VoidPaymentRequest("Lo anulo yo"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await owner.PostAsync($"/api/payments/void-requests/{pending.Id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await staff.PostAsJsonAsync($"/api/payments/{payment.Id}/void-requests", new RequestVoidRequest("Tercera"))).StatusCode);
    }

    // ---- campana del staff ----

    [Fact]
    public async Task A_request_creates_one_action_notice_in_the_staff_bell_that_is_resolved_with_the_decision()
    {
        var w = await SeedAsync();
        await using var host = RealIdentityHost();
        var owner = await LoggedInAsync(host, w.OwnerEmail);
        var staff = await LoggedInAsync(host, w.StaffEmail);
        var payment = await RecordAsync(staff, 33_000m);
        var request = await RequestAsync(staff, payment.Id, "Error de monto");

        var open = (await owner.GetFromJsonAsync<PagedResult<StaffNotificationItem>>("/api/staff/notifications?openOnly=true"))!;
        var notice = Assert.Single(open.Items, n => n.Type == "VoidRequested");
        Assert.True(notice.RequiresAction);
        Assert.Contains("Error de monto", notice.Body);
        Assert.Equal("/payments", notice.Link);

        await owner.PostAsync($"/api/payments/void-requests/{request.Id}/reject", null);

        var after = (await owner.GetFromJsonAsync<PagedResult<StaffNotificationItem>>("/api/staff/notifications?openOnly=true"))!;
        Assert.DoesNotContain(after.Items, n => n.Type == "VoidRequested");
        var all = (await owner.GetFromJsonAsync<PagedResult<StaffNotificationItem>>("/api/staff/notifications?openOnly=false"))!;
        var resolved = Assert.Single(all.Items, n => n.Type == "VoidRequested");
        Assert.Equal(w.OwnerId, resolved.ResolvedBy);
    }

    // ---- ayudas ----

    private static async Task<PaymentResponse> RecordAsync(HttpClient client, decimal amount)
    {
        var response = await client.PostAsJsonAsync(
            "/api/payments",
            new RecordPaymentRequest(amount, PaymentMethod.Cash, "Venta de mostrador", null, null, null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PaymentResponse>())!;
    }

    private static async Task<VoidRequestResponse> RequestAsync(HttpClient client, Guid paymentId, string reason)
    {
        var response = await client.PostAsJsonAsync($"/api/payments/{paymentId}/void-requests", new RequestVoidRequest(reason));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<VoidRequestResponse>())!;
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
        var gym = Gym.Create("Pay Perm Gym", $"payperm-{suffix}");
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id));
        await db.SaveChangesAsync();

        var ownerEmail = $"owner-{suffix}@test.local";
        var staffEmail = $"staff-{suffix}@test.local";
        var ownerId = await provisioning.CreateGymUserAsync(ownerEmail, "Pay Owner", Password, UserRole.Owner, gym.Id);
        var staffId = await provisioning.CreateGymUserAsync(staffEmail, "Pay Staff", Password, UserRole.Staff, gym.Id);
        gym.AssignOwner(ownerId);
        await db.SaveChangesAsync();
        return new World(ownerId, ownerEmail, staffId, staffEmail);
    }

    private sealed record World(Guid OwnerId, string OwnerEmail, Guid StaffId, string StaffEmail);
}
