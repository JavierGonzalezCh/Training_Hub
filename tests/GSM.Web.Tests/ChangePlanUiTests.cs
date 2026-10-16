using AngleSharp.Dom;
using Bunit;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Members;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>UI de MS6: el diálogo de cambiar plan muestra el crédito antes de confirmar y mueve el dinero con una segunda llamada a Payments.</summary>
public class ChangePlanUiTests : TestContext
{
    private static readonly DateOnly Start = new(2026, 10, 8);
    private static readonly DateOnly End = new(2026, 11, 6);

    private readonly Guid _memberId = Guid.NewGuid();
    private readonly PlanResponse _current = new() { Id = Guid.NewGuid(), Name = "Mensual", Price = 90000, DurationUnit = "days", DurationValue = 30, IsActive = true };
    private readonly PlanResponse _target = new() { Id = Guid.NewGuid(), Name = "Trimestral", Price = 120000, DurationUnit = "days", DurationValue = 90, IsActive = true };
    private readonly MembershipResponse _membership;
    private CommercialStatusUiTests.FakeClient _membershipsFake = null!;
    private CommercialStatusUiTests.FakeClient _paymentsFake = null!;
    private IRenderedComponent<MudDialogProvider> _provider = null!;

    public ChangePlanUiTests()
    {
        _membership = new MembershipResponse
        {
            Id = Guid.NewGuid(), MemberId = _memberId, PlanId = _current.Id, PlanName = "Mensual", Status = "Active",
            StartDate = Start, EndDate = End, AgreedPrice = 90000, TotalToCharge = 90000,
        };
    }

    private PlanChangePreviewResponse Preview(double credit, double agreedPrice, double? refund = null) => new()
    {
        CurrentMembershipId = _membership.Id, CurrentPlanName = "Mensual", CurrentEndDate = End,
        AmountPaid = 90000, CreditBase = 90000, CalculatedCredit = 60000, Credit = credit,
        CreditApplied = Math.Min(credit, agreedPrice), DaysLeft = 20, DaysTotal = 30,
        NewPlanId = _target.Id, NewPlanName = "Trimestral", PlanPrice = 120000, AgreedPrice = agreedPrice,
        StartDate = Start.AddDays(10), EndDate = Start.AddDays(99),
        TotalToCharge = Math.Max(0, agreedPrice - credit), RefundAmount = refund ?? Math.Max(0, credit - agreedPrice),
    };

    private async Task OpenAsync(Func<ChangePlanRequest, PlanChangePreviewResponse>? preview = null)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();

        var plans = System.Reflection.DispatchProxy.Create<IMembershipPlansClient, CommercialStatusUiTests.FakeClient>();
        ((CommercialStatusUiTests.FakeClient)(object)plans).Handlers["MembershipPlansAllAsync"] = _ => new List<PlanResponse> { _current, _target };
        var memberships = System.Reflection.DispatchProxy.Create<IMemberMembershipsClient, CommercialStatusUiTests.FakeClient>();
        _membershipsFake = (CommercialStatusUiTests.FakeClient)(object)memberships;
        _membershipsFake.Handlers["PreviewPlanChangeAsync"] = args =>
            (preview ?? (r => Preview(60000, r.AgreedPrice ?? 120000)))((ChangePlanRequest)args[2]!);
        var payments = System.Reflection.DispatchProxy.Create<IPaymentsClient, CommercialStatusUiTests.FakeClient>();
        _paymentsFake = (CommercialStatusUiTests.FakeClient)(object)payments;
        Services.AddSingleton(plans);
        Services.AddSingleton(memberships);
        Services.AddSingleton(payments);

        RenderComponent<MudPopoverProvider>();
        _provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<ChangePlanDialog>
        {
            { x => x.MemberId, _memberId },
            { x => x.Membership, _membership },
        };
        await _provider.InvokeAsync(() => dialogs.ShowAsync<ChangePlanDialog>("Cambiar plan", parameters));
        _provider.WaitForAssertion(() => Assert.NotEmpty(_provider.FindAll("#total-to-charge")));
    }

    private IElement Input(string id)
    {
        var inner = _provider.FindAll($"#{id} input");
        return inner.Count > 0 ? inner[0] : _provider.Find($"#{id}");
    }

    private void Click(string text)
        => _provider.FindAll("button").First(b => b.TextContent.Contains(text)).Click();

    private ChangePlanResponse Changed(double credit, double total, double refund) => new()
    {
        Previous = new MembershipResponse { Id = _membership.Id, MemberId = _memberId, PlanName = "Mensual", Status = "Cancelled", StartDate = Start, EndDate = End },
        Current = new MembershipResponse
        {
            Id = Guid.NewGuid(), MemberId = _memberId, PlanId = _target.Id, PlanName = "Trimestral", Status = "Active",
            StartDate = Start.AddDays(10), EndDate = Start.AddDays(99), CreditApplied = credit, TotalToCharge = total,
        },
        CreditApplied = credit, TotalToCharge = total, RefundAmount = refund,
    };

    [Fact]
    public async Task The_dialog_shows_the_credit_the_new_dates_and_the_total_before_confirming()
    {
        await OpenAsync();

        // Propone el primer plan distinto del actual y pide la vista previa al servidor.
        Assert.Contains(_membershipsFake.Calls, c => c.Method == "PreviewPlanChangeAsync");
        var request = (ChangePlanRequest)_membershipsFake.Calls.Last(c => c.Method == "PreviewPlanChangeAsync").Args[2]!;
        Assert.Equal(_target.Id, request.PlanId);

        var preview = _provider.Find("#plan-change-preview").TextContent;
        Assert.Contains("20 de 30 días", preview);
        Assert.Contains("60", _provider.Find("#credit-amount").TextContent);
        Assert.Contains("60", _provider.Find("#total-to-charge").TextContent);
        Assert.Contains("Cambiar plan y cobrar", _provider.Markup);
        Assert.Empty(_membershipsFake.Calls.Where(c => c.Method == "ChangePlanAsync"));
    }

    [Fact]
    public async Task Confirming_changes_the_plan_and_charges_the_difference_linked_to_the_new_membership()
    {
        await OpenAsync();
        var changed = Changed(credit: 60000, total: 60000, refund: 0);
        _membershipsFake.Handlers["ChangePlanAsync"] = _ => changed;
        _paymentsFake.Handlers["PaymentsPOST2Async"] = _ => new PaymentResponse();

        Click("Cambiar plan y cobrar");

        _provider.WaitForAssertion(() => Assert.Contains(_paymentsFake.Calls, c => c.Method == "PaymentsPOST2Async"));
        var payment = (RecordPaymentRequest)_paymentsFake.Calls.Single(c => c.Method == "PaymentsPOST2Async").Args[0]!;
        Assert.Equal(60000, payment.Amount);
        Assert.Equal(_memberId, payment.MemberId);
        Assert.Equal(changed.Current.Id, payment.MembershipId);
        Assert.Equal(_membership.Id, (Guid)_membershipsFake.Calls.Single(c => c.Method == "ChangePlanAsync").Args[1]!);
    }

    [Fact]
    public async Task When_the_credit_exceeds_the_new_price_the_refund_is_a_negative_payment_linked_to_the_old_membership()
    {
        await OpenAsync(r => Preview(credit: 200000, agreedPrice: r.AgreedPrice ?? 120000));
        Assert.Contains("A devolver", _provider.Find("#total-to-charge").TextContent);
        Assert.Contains("Cambiar plan y devolver", _provider.Markup);

        var changed = Changed(credit: 120000, total: 0, refund: 80000);
        _membershipsFake.Handlers["ChangePlanAsync"] = _ => changed;
        _paymentsFake.Handlers["PaymentsPOST2Async"] = _ => new PaymentResponse();

        Click("Cambiar plan y devolver");

        _provider.WaitForAssertion(() => Assert.Contains(_paymentsFake.Calls, c => c.Method == "PaymentsPOST2Async"));
        var payment = (RecordPaymentRequest)_paymentsFake.Calls.Single(c => c.Method == "PaymentsPOST2Async").Args[0]!;
        Assert.Equal(-80000, payment.Amount);
        Assert.Equal(_membership.Id, payment.MembershipId);
        Assert.Contains("Devolución", payment.Concept);
    }

    [Fact]
    public async Task A_zero_total_changes_the_plan_without_any_payment()
    {
        await OpenAsync(r => Preview(credit: 120000, agreedPrice: r.AgreedPrice ?? 120000));
        _membershipsFake.Handlers["ChangePlanAsync"] = _ => Changed(credit: 120000, total: 0, refund: 0);

        Click("Cambiar plan");

        _provider.WaitForAssertion(() => Assert.Contains(_membershipsFake.Calls, c => c.Method == "ChangePlanAsync"));
        Assert.DoesNotContain(_paymentsFake.Calls, c => c.Method == "PaymentsPOST2Async");
    }

    [Fact]
    public async Task Adjusting_the_credit_asks_for_a_reason_and_sends_the_override()
    {
        await OpenAsync();

        Input("adjust-credit").Change(true);
        _provider.WaitForAssertion(() => Assert.NotEmpty(_provider.FindAll("#credit-reason")));

        Click("Cambiar plan y cobrar");
        _provider.WaitForAssertion(() => Assert.Contains("motivo del ajuste", _provider.Markup));
        Assert.DoesNotContain(_membershipsFake.Calls, c => c.Method == "ChangePlanAsync");

        Input("credit-override").Change("40000");
        Input("credit-reason").Change("Compensación por máquina dañada");
        _membershipsFake.Handlers["ChangePlanAsync"] = _ => Changed(credit: 40000, total: 80000, refund: 0);
        _paymentsFake.Handlers["PaymentsPOST2Async"] = _ => new PaymentResponse();
        Click("Cambiar plan y cobrar");

        _provider.WaitForAssertion(() => Assert.Contains(_membershipsFake.Calls, c => c.Method == "ChangePlanAsync"));
        var request = (ChangePlanRequest)_membershipsFake.Calls.Last(c => c.Method == "ChangePlanAsync").Args[2]!;
        Assert.Equal(40000, request.CreditOverride);
        Assert.Equal("Compensación por máquina dañada", request.CreditAdjustmentReason);
    }

    [Fact]
    public async Task Lowering_the_price_asks_for_a_discount_reason_before_confirming()
    {
        await OpenAsync();
        Assert.Empty(_provider.FindAll("#discount-reason"));

        Input("agreed-price").Change("100000");
        _provider.WaitForAssertion(() => Assert.NotEmpty(_provider.FindAll("#discount-reason")));

        Click("Cambiar plan y cobrar");
        _provider.WaitForAssertion(() => Assert.Contains("motivo del descuento", _provider.Markup));
        Assert.DoesNotContain(_membershipsFake.Calls, c => c.Method == "ChangePlanAsync");
    }

    [Fact]
    public async Task When_the_payment_fails_the_dialog_stays_open_and_offers_retry()
    {
        await OpenAsync();
        _membershipsFake.Handlers["ChangePlanAsync"] = _ => Changed(credit: 60000, total: 60000, refund: 0);
        _paymentsFake.Handlers["PaymentsPOST2Async"] = _ => throw new GsmApiException("Conflict", 409, "{\"detail\":\"Caja cerrada\"}", new Dictionary<string, IEnumerable<string>>(), null);

        Click("Cambiar plan y cobrar");

        _provider.WaitForAssertion(() => Assert.NotEmpty(_provider.FindAll("#payment-failed")));
        Assert.Contains("Reintentar", _provider.Markup);
        Assert.Contains("Cerrar sin registrarlo", _provider.Markup);

        // El cambio ya se hizo: reintentar solo repite el cobro, no vuelve a cambiar el plan.
        _paymentsFake.Handlers["PaymentsPOST2Async"] = _ => new PaymentResponse();
        Click("Reintentar");
        _provider.WaitForAssertion(() => Assert.Equal(2, _paymentsFake.Calls.Count(c => c.Method == "PaymentsPOST2Async")));
        Assert.Single(_membershipsFake.Calls, c => c.Method == "ChangePlanAsync");
    }
}
