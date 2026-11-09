using System.Reflection;
using Bunit;
using Bunit.TestDoubles;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.CashRegister;
using GSM.Panel.UI.Pages.Payments;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using FakeClient = GSM.Web.Tests.PaymentPermissionsUiTests.FakeClient;

namespace GSM.Web.Tests;

/// <summary>P3: barra de la caja, abrir, cerrar con arqueo, anulación en efectivo y reporte imprimible.</summary>
public class CashDrawerUiTests : TestContext
{
    private readonly ICashShiftsClient _cash;
    private readonly FakeClient _cashFake;
    private readonly IPaymentsClient _payments;
    private readonly FakeClient _paymentsFake;

    public CashDrawerUiTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        _cash = DispatchProxy.Create<ICashShiftsClient, FakeClient>();
        _cashFake = (FakeClient)(object)_cash;
        _payments = DispatchProxy.Create<IPaymentsClient, FakeClient>();
        _paymentsFake = (FakeClient)(object)_payments;
        Services.AddSingleton(_cash);
        Services.AddSingleton(_payments);
        var settings = DispatchProxy.Create<IPaymentSettingsClient, FakeClient>();
        ((FakeClient)(object)settings).Handlers["PaymentSettingsGETAsync"] = _ => new PaymentSettingsResponse { UseCashDrawer = true };
        Services.AddSingleton(settings);
        Services.AddScoped<CashDrawerState>();
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("ana");
        auth.SetRoles("staff");
    }

    private static CashShiftResponse OpenShift(double expected = 340_000, bool stale = false) => new()
    {
        Id = Guid.NewGuid(), OpenedBy = Guid.NewGuid(), OpenedByName = "Ana", OpenedAt = DateTimeOffset.UtcNow.AddHours(-3),
        OpeningCash = 50_000, ExpectedCash = expected, IsOpen = true, IsStale = stale,
    };

    // ---- barra ----

    [Fact]
    public void Bar_without_an_open_shift_offers_to_open_it()
    {
        _cashFake.Handlers["CurrentAsync"] = _ => new CashDrawerStatusResponse { Shift = null, SuggestedOpeningCash = 20_000 };

        var cut = RenderComponent<CashDrawerBar>();

        Assert.Single(cut.FindAll("[data-testid='cash-drawer-closed']"));
        Assert.Single(cut.FindAll("[data-testid='open-cash']"));
        Assert.Empty(cut.FindAll("[data-testid='close-cash']"));
    }

    [Fact]
    public void Bar_with_an_open_shift_shows_who_opened_it_and_the_expected_cash()
    {
        _cashFake.Handlers["CurrentAsync"] = _ => new CashDrawerStatusResponse { Shift = OpenShift(340_000) };

        var cut = RenderComponent<CashDrawerBar>();

        var text = cut.Find("[data-testid='cash-drawer-open']").TextContent;
        Assert.Contains("Caja abierta por Ana", text);
        Assert.Contains("340", text);
        Assert.Single(cut.FindAll("[data-testid='close-cash']"));
        Assert.Single(cut.FindAll("[data-testid='cash-movement']"));
        Assert.Empty(cut.FindAll("[data-testid='open-cash']"));
        Assert.Empty(cut.FindAll("[data-testid='cash-drawer-stale']"));
    }

    [Fact]
    public void Bar_flags_a_shift_forgotten_for_more_than_sixteen_hours()
    {
        _cashFake.Handlers["CurrentAsync"] = _ => new CashDrawerStatusResponse { Shift = OpenShift(stale: true) };

        var cut = RenderComponent<CashDrawerBar>();

        Assert.Single(cut.FindAll("[data-testid='cash-drawer-stale']"));
    }

    // ---- abrir ----

    [Fact]
    public async Task Open_dialog_is_prefilled_with_the_last_counted_cash_and_sends_the_base()
    {
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        await provider.InvokeAsync(() => dialogs.ShowAsync<OpenCashDialog>(
            "Abrir", new DialogParameters<OpenCashDialog> { { x => x.SuggestedBase, 20_000m } }));

        provider.Find("[data-testid='confirm-open-cash']").Click();

        var call = Assert.Single(_cashFake.Calls, c => c.Method == "OpenAsync");
        Assert.Equal(20_000d, ((OpenCashShiftRequest)call.Args[0]!).OpeningCash);
    }

    // ---- cerrar ----

    [Fact]
    public async Task Close_dialog_is_a_blind_count_and_a_balanced_close_goes_through_without_a_note()
    {
        var shift = OpenShift(125_000);
        _cashFake.Handlers["CloseAsync"] = _ => new CashShiftReportResponse { Shift = shift };

        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        await provider.InvokeAsync(() => dialogs.ShowAsync<CloseCashDialog>(
            "Cerrar", new DialogParameters<CloseCashDialog> { { x => x.Shift, shift } }));

        // A ciegas: sin conteo no se ve lo esperado ni se puede cerrar.
        Assert.Empty(provider.FindAll("[data-testid='expected-cash']"));
        Assert.NotNull(provider.Find("[data-testid='expected-hidden']"));
        Assert.True(provider.Find("[data-testid='confirm-close-cash']").HasAttribute("disabled"));

        provider.Find("input").Input("125000");
        Assert.Contains("125", provider.Find("[data-testid='expected-cash']").TextContent);
        Assert.Contains("La caja cuadra", provider.Find("[data-testid='cash-difference']").TextContent);

        provider.Find("[data-testid='confirm-close-cash']").Click();

        var call = Assert.Single(_cashFake.Calls, c => c.Method == "CloseAsync");
        var body = (CloseCashShiftRequest)call.Args[0]!;
        Assert.Equal(125_000d, body.CountedCash);
        Assert.Null(body.Notes);
    }

    [Fact]
    public async Task Close_dialog_requires_a_note_when_the_count_differs_and_does_not_call_the_api_without_it()
    {
        var shift = OpenShift(125_000);
        _cashFake.Handlers["CloseAsync"] = _ => new CashShiftReportResponse { Shift = shift };

        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        await provider.InvokeAsync(() => dialogs.ShowAsync<CloseCashDialog>(
            "Cerrar", new DialogParameters<CloseCashDialog> { { x => x.Shift, shift } }));

        provider.Find("input").Input("120000");
        Assert.Contains("Faltante", provider.Find("[data-testid='cash-difference']").TextContent);
        // El botón nombra la consecuencia en vez de un "Cerrar caja" neutro.
        Assert.Contains("Cerrar con faltante de", provider.Find("[data-testid='confirm-close-cash']").TextContent);

        provider.Find("[data-testid='confirm-close-cash']").Click();
        Assert.Contains("explique la diferencia", provider.Find("[data-testid='close-cash-error']").TextContent);
        Assert.DoesNotContain(_cashFake.Calls, c => c.Method == "CloseAsync");

        provider.Find("textarea").Change("Se pagó un domicilio");
        provider.Find("[data-testid='confirm-close-cash']").Click();

        var body = (CloseCashShiftRequest)Assert.Single(_cashFake.Calls, c => c.Method == "CloseAsync").Args[0]!;
        Assert.Equal(120_000d, body.CountedCash);
        Assert.Equal("Se pagó un domicilio", body.Notes);
    }

    // ---- anular en efectivo ----

    private static PaymentResponse CashPayment(Guid? shiftId) => new()
    {
        Id = Guid.NewGuid(), Amount = 30_000, Method = "Cash", Concept = "Mensualidad", RegisteredBy = Guid.NewGuid(),
        PaidAt = DateTimeOffset.UtcNow, ShiftId = shiftId,
    };

    [Fact]
    public async Task Voiding_cash_of_a_closed_shift_asks_if_the_money_is_returned_and_blocks_without_an_answer()
    {
        _cashFake.Handlers["CurrentAsync"] = _ => new CashDrawerStatusResponse { Shift = OpenShift() };
        var payment = CashPayment(Guid.NewGuid()); // de otro turno

        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        await provider.InvokeAsync(() => dialogs.ShowAsync<VoidPaymentDialog>(
            "Anular", new DialogParameters<VoidPaymentDialog> { { x => x.Payment, payment } }));

        Assert.Single(provider.FindAll("[data-testid='refund-question']"));

        provider.Find("textarea").Change("Cobro duplicado");
        provider.Find("button.mud-button-filled-error").Click();

        Assert.DoesNotContain(_paymentsFake.Calls, c => c.Method == "VoidAsync");
        Assert.Contains("Indica si el dinero se devuelve", provider.Markup);
    }

    [Fact]
    public async Task Voiding_cash_of_the_open_shift_does_not_ask_about_the_refund()
    {
        var shift = OpenShift();
        _cashFake.Handlers["CurrentAsync"] = _ => new CashDrawerStatusResponse { Shift = shift };
        var payment = CashPayment(shift.Id);
        payment.MemberId = Guid.NewGuid();
        payment.MemberName = "Ana Prueba";

        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        await provider.InvokeAsync(() => dialogs.ShowAsync<VoidPaymentDialog>(
            "Anular", new DialogParameters<VoidPaymentDialog> { { x => x.Payment, payment } }));

        Assert.Empty(provider.FindAll("[data-testid='refund-question']"));
        // Antes de anular se reconoce el pago: de quién y por cuánto.
        var summary = provider.Find("[data-testid='void-payment-summary']").TextContent;
        Assert.Contains("Ana Prueba", summary);
        Assert.Contains("30.000", summary);

        provider.Find("textarea").Change("Error de cobro");
        provider.Find("button.mud-button-filled-error").Click();

        var body = (VoidPaymentRequest)Assert.Single(_paymentsFake.Calls, c => c.Method == "VoidAsync").Args[1]!;
        Assert.Null(body.RefundNow);
    }

    [Fact]
    public async Task Voiding_a_non_cash_payment_never_asks_about_the_refund()
    {
        var payment = CashPayment(Guid.NewGuid());
        payment.Method = "Nequi";

        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        await provider.InvokeAsync(() => dialogs.ShowAsync<VoidPaymentDialog>(
            "Anular", new DialogParameters<VoidPaymentDialog> { { x => x.Payment, payment } }));

        Assert.Empty(provider.FindAll("[data-testid='refund-question']"));
        Assert.DoesNotContain(_cashFake.Calls, c => c.Method == "CurrentAsync");
    }

    // ---- bandeja ----

    [Fact]
    public async Task Approving_a_cash_request_of_a_closed_shift_asks_first_and_sends_the_answer()
    {
        var request = new VoidRequestResponse
        {
            Id = Guid.NewGuid(), PaymentId = Guid.NewGuid(), Reason = "Duplicado", RequestedBy = Guid.NewGuid(),
            RequestedByName = "Beto", RequestedAt = DateTimeOffset.UtcNow, Status = "pending",
            PaymentConcept = "Mensualidad", PaymentAmount = 30_000, PaymentMethod = "Cash",
            PaymentPaidAt = DateTimeOffset.UtcNow, RequiresRefundDecision = true,
        };
        _paymentsFake.Handlers["VoidRequestsGETAsync"] = _ => new PagedResultOfVoidRequestResponse { Items = [request], Total = 1 };
        _paymentsFake.Handlers["ApproveAsync"] = _ => new PaymentResponse();

        var provider = RenderComponent<MudDialogProvider>();
        var inbox = RenderComponent<VoidRequestsInbox>();

        await inbox.InvokeAsync(() => inbox.Find("[data-testid='approve-void']").Click());
        Assert.DoesNotContain(_paymentsFake.Calls, c => c.Method == "ApproveAsync");
        Assert.Contains("¿Se devuelve el dinero ahora?", provider.Markup);

        provider.FindAll("button").First(b => b.TextContent.Contains("No, nunca estuvo")).Click();

        var approve = Assert.Single(_paymentsFake.Calls, c => c.Method == "ApproveAsync");
        Assert.Equal(request.Id, approve.Args[0]);
        Assert.False(Assert.IsType<ApproveVoidRequestRequest>(approve.Args[1]).RefundNow);
    }

    // ---- reporte imprimible ----

    [Fact]
    public void Shift_report_shows_the_arqueo_the_reversals_and_prints()
    {
        var shift = OpenShift(125_000);
        shift.IsOpen = false;
        shift.ClosedAt = DateTimeOffset.UtcNow;
        shift.ClosedByName = "Ana";
        shift.CountedCash = 120_000;
        shift.Difference = -5_000;
        shift.CloseNotes = "Faltan 5 mil";
        var reversal = CashPayment(shift.Id);
        reversal.Amount = -30_000;
        reversal.Concept = "Anulación: Mensualidad";
        reversal.Notes = "Cobro duplicado";
        reversal.ReversesPaymentId = Guid.NewGuid();
        reversal.AffectsCashDrawer = false;

        _cashFake.Handlers["ReportAsync"] = _ => new CashShiftReportResponse
        {
            Shift = shift, CashIn = 100_000, CashOut = 15_000, Deposits = 10_000, Withdrawals = 20_000,
            Methods = [new CashMethodSummary { Method = "Cash", Total = 85_000, Count = 2 }],
            Movements = [], Payments = [reversal], Reversals = [reversal],
        };

        var cut = RenderComponent<ShiftReport>(p => p.Add(x => x.ShiftId, shift.Id));

        Assert.Contains("125", cut.Find("[data-testid='report-expected']").TextContent);
        Assert.Contains("120", cut.Find("[data-testid='report-counted']").TextContent);
        Assert.Contains("5", cut.Find("[data-testid='report-difference']").TextContent);
        Assert.Contains("Faltan 5 mil", cut.Markup);
        Assert.Contains("NO TOCA LA CAJA", cut.Find("[data-testid='report-reversals']").TextContent);

        cut.Find("[data-testid='print-report']").Click();
        JSInterop.VerifyInvoke("print");
    }
}
