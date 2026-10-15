using System.Reflection;
using Bunit;
using Bunit.TestDoubles;
using GSM.Client;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Payments;
using GSM.UI.Shared.Auth;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using PaymentsPage = GSM.Panel.UI.Pages.Payments.Index;

namespace GSM.Web.Tests;

/// <summary>
/// P2: lo que Payments muestra u oculta según los permisos <c>ViewFinances</c> y <c>VoidPayments</c>, la solicitud
/// de anulación y la bandeja de solicitudes pendientes.
/// </summary>
public class PaymentPermissionsUiTests : TestContext
{
    public class FakeClient : DispatchProxy
    {
        public Dictionary<string, Func<object?[], object?>> Handlers { get; } = [];

        public List<(string Method, object?[] Args)> Calls { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var name = targetMethod!.Name;
            Calls.Add((name, args ?? []));
            var result = Handlers.TryGetValue(name, out var handler) ? handler(args ?? []) : null;

            var returnType = targetMethod.ReturnType;
            if (!returnType.IsGenericType)
            {
                return Task.CompletedTask;
            }

            var inner = returnType.GetGenericArguments()[0];
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, [result]);
        }
    }

    private readonly IPaymentsClient _payments;
    private readonly FakeClient _fake;
    private readonly TestAuthorizationContext _auth;

    public PaymentPermissionsUiTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        _payments = DispatchProxy.Create<IPaymentsClient, FakeClient>();
        _fake = (FakeClient)(object)_payments;
        Services.AddSingleton(_payments);
        // P3: la página de pagos muestra la barra de la caja (sin respuesta del fake: "caja cerrada").
        Services.AddSingleton(DispatchProxy.Create<ICashShiftsClient, FakeClient>());
        _auth = this.AddTestAuthorization();
        _auth.SetAuthorized("luis");
        _auth.SetRoles("staff");
    }

    private void Grant(params string[] permissions)
        => _auth.SetPolicies(permissions.Select(GsmPolicies.ForPermission).ToArray());

    private static PaymentResponse Payment(bool reversed = false, bool pending = false, Guid? reverses = null) => new()
    {
        Id = Guid.NewGuid(), Amount = 90000, Method = "Cash", Concept = "Mensualidad", RegisteredBy = Guid.NewGuid(),
        PaidAt = DateTimeOffset.UtcNow, IsReversed = reversed, HasPendingVoidRequest = pending, ReversesPaymentId = reverses,
    };

    private static VoidRequestResponse PendingRequest(string by = "Ana Recepción") => new()
    {
        Id = Guid.NewGuid(), PaymentId = Guid.NewGuid(), Reason = "Cobro duplicado", RequestedBy = Guid.NewGuid(),
        RequestedByName = by, RequestedAt = DateTimeOffset.UtcNow, Status = "pending", PaymentConcept = "Mensualidad",
        PaymentAmount = 90000, PaymentMethod = "Cash", PaymentPaidAt = DateTimeOffset.UtcNow,
    };

    private static PagedResultOfPaymentResponse EmptyPayments() => new() { Items = [], Page = 1, PageSize = 20, Total = 0 };

    // ---- acción por pago ----

    [Fact]
    public void Staff_without_VoidPayments_sees_request_instead_of_void()
    {
        var cut = RenderComponent<PaymentVoidAction>(p => p.Add(x => x.Payment, Payment()));

        Assert.Single(cut.FindAll("[data-testid='request-void']"));
        Assert.Empty(cut.FindAll("[data-testid='void-payment']"));
    }

    [Fact]
    public void Staff_with_VoidPayments_sees_the_direct_void()
    {
        Grant("void_payments");

        var cut = RenderComponent<PaymentVoidAction>(p => p.Add(x => x.Payment, Payment()));

        Assert.Single(cut.FindAll("[data-testid='void-payment']"));
        Assert.Empty(cut.FindAll("[data-testid='request-void']"));
    }

    [Fact]
    public void A_payment_with_a_pending_request_shows_the_requested_chip_and_no_buttons()
    {
        Grant("void_payments");

        var cut = RenderComponent<PaymentVoidAction>(p => p.Add(x => x.Payment, Payment(pending: true)));

        Assert.Contains("ANULACIÓN SOLICITADA", cut.Markup);
        Assert.Empty(cut.FindAll("[data-testid='void-payment']"));
        Assert.Empty(cut.FindAll("[data-testid='request-void']"));
    }

    [Fact]
    public void Reversed_payments_and_reversals_keep_their_chips()
    {
        var reversed = RenderComponent<PaymentVoidAction>(p => p.Add(x => x.Payment, Payment(reversed: true)));
        var reversal = RenderComponent<PaymentVoidAction>(p => p.Add(x => x.Payment, Payment(reverses: Guid.NewGuid())));

        Assert.Contains("ANULADO", reversed.Markup);
        Assert.Contains("ANULACIÓN", reversal.Markup);
        Assert.Empty(reversed.FindAll("[data-testid='request-void']"));
    }

    [Fact]
    public async Task Request_dialog_sends_the_reason_and_rejects_an_empty_one_without_calling_the_api()
    {
        var payment = Payment();
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        await provider.InvokeAsync(() => dialogs.ShowAsync<RequestVoidDialog>(
            "Solicitar", new DialogParameters<RequestVoidDialog> { { x => x.Payment, payment } }));

        provider.Find("[data-testid='send-void-request']").Click();
        Assert.Contains("Escribe el motivo", provider.Markup);
        Assert.DoesNotContain(_fake.Calls, c => c.Method == "VoidRequestsPOSTAsync");

        provider.Find("textarea").Change("Cobro duplicado");
        provider.Find("[data-testid='send-void-request']").Click();

        var call = Assert.Single(_fake.Calls, c => c.Method == "VoidRequestsPOSTAsync");
        Assert.Equal(payment.Id, call.Args[0]);
        Assert.Equal("Cobro duplicado", ((RequestVoidRequest)call.Args[1]!).Reason);
    }

    // ---- bandeja de solicitudes ----

    [Fact]
    public void Payments_page_only_loads_the_inbox_for_whoever_can_resolve_requests()
    {
        _fake.Handlers["VoidRequestsGETAsync"] = _ => new PagedResultOfVoidRequestResponse { Items = [PendingRequest()], Total = 1 };

        var without = RenderComponent<PaymentsPage>();
        Assert.Empty(without.FindAll("[data-testid='void-requests-inbox']"));
        Assert.DoesNotContain(_fake.Calls, c => c.Method == "VoidRequestsGETAsync");

        Grant("void_payments");
        var with = RenderComponent<PaymentsPage>();
        Assert.Single(with.FindAll("[data-testid='void-requests-inbox']"));
    }

    [Fact]
    public void Inbox_lists_pending_requests_with_who_asked_and_why()
    {
        Grant("void_payments");
        _fake.Handlers["VoidRequestsGETAsync"] = _ => new PagedResultOfVoidRequestResponse
        {
            Items = [PendingRequest("Ana Recepción"), PendingRequest("Beto")], Total = 2,
        };

        var cut = RenderComponent<VoidRequestsInbox>();

        Assert.Equal(2, cut.FindAll("[data-testid='void-request']").Count);
        Assert.Contains("Ana Recepción pidió anularlo", cut.Markup);
        Assert.Contains("Motivo: Cobro duplicado", cut.Markup);
        var list = Assert.Single(_fake.Calls, c => c.Method == "VoidRequestsGETAsync");
        Assert.Equal("pending", list.Args[0]);
    }

    [Fact]
    public void Approving_calls_the_approve_endpoint_and_reloads_the_inbox()
    {
        Grant("void_payments");
        var first = PendingRequest();
        var resolved = false;
        _fake.Handlers["VoidRequestsGETAsync"] = _ => new PagedResultOfVoidRequestResponse
        {
            Items = resolved ? [] : [first], Total = resolved ? 0 : 1,
        };
        _fake.Handlers["ApproveAsync"] = _ => { resolved = true; return new PaymentResponse(); };

        var cut = RenderComponent<VoidRequestsInbox>();
        cut.Find("[data-testid='approve-void']").Click();

        var approve = Assert.Single(_fake.Calls, c => c.Method == "ApproveAsync");
        Assert.Equal(first.Id, approve.Args[0]);
        Assert.Equal(2, _fake.Calls.Count(c => c.Method == "VoidRequestsGETAsync"));
        Assert.Empty(cut.FindAll("[data-testid='void-request']"));
    }

    [Fact]
    public void Rejecting_calls_the_reject_endpoint_and_not_approve()
    {
        Grant("void_payments");
        var first = PendingRequest();
        _fake.Handlers["VoidRequestsGETAsync"] = _ => new PagedResultOfVoidRequestResponse { Items = [first], Total = 1 };

        var cut = RenderComponent<VoidRequestsInbox>();
        cut.Find("[data-testid='reject-void']").Click();

        // Rechazar abre la nota (opcional) y pide confirmar; todavía no llama a la API.
        Assert.DoesNotContain(_fake.Calls, c => c.Method == "RejectAsync");
        cut.Find("textarea").Input("  Pagó dos meses  ");
        cut.Find("[data-testid='confirm-reject']").Click();

        var reject = Assert.Single(_fake.Calls, c => c.Method == "RejectAsync");
        Assert.Equal(first.Id, reject.Args[0]);
        Assert.Equal("Pagó dos meses", Assert.IsType<RejectVoidRequestRequest>(reject.Args[1]).Note);
        Assert.DoesNotContain(_fake.Calls, c => c.Method == "ApproveAsync");
    }

    // ---- la página de pagos ----

    [Fact]
    public void Payments_page_without_ViewFinances_does_not_list_the_gym_payments()
    {
        var cut = RenderComponent<PaymentsPage>();

        Assert.Single(cut.FindAll("[data-testid='no-finances']"));
        Assert.DoesNotContain(_fake.Calls, c => c.Method.StartsWith("PaymentsGET"));
        Assert.DoesNotContain("Cierre de caja", cut.Markup);
        Assert.Contains("Registrar pago", cut.Markup);
    }

    [Fact]
    public void Payments_page_with_ViewFinances_lists_payments_and_links_the_daily_close()
    {
        Grant("view_finances");
        _fake.Handlers["PaymentsGET3Async"] = _ => new PagedResultOfPaymentResponse
        {
            Items = [Payment()], Page = 1, PageSize = 20, Total = 1,
        };

        RenderComponent<MudPopoverProvider>();
        var cut = RenderComponent<PaymentsPage>();

        Assert.Empty(cut.FindAll("[data-testid='no-finances']"));
        Assert.Contains(_fake.Calls, c => c.Method == "PaymentsGET3Async");
        Assert.Contains("Cierre de caja", cut.Markup);
        Assert.Contains("Mensualidad", cut.Markup);
    }
}
