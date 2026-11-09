using System.Reflection;
using Bunit;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Payments;
using GSM.UI.Shared.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using FakeClient = GSM.Web.Tests.PaymentPermissionsUiTests.FakeClient;

namespace GSM.Web.Tests;

/// <summary>P1/P2 — cobrar en dos toques, abrir la caja desde el cobro y el tooltip de información.</summary>
public class ChargeFlowUiTests : TestContext
{
    private FakeClient _cashFake = null!;
    private FakeClient _memberships = null!;

    private void Setup(bool useCashDrawer = true, bool shiftOpen = false)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var cash = DispatchProxy.Create<ICashShiftsClient, FakeClient>();
        _cashFake = (FakeClient)(object)cash;
        _cashFake.Handlers["CurrentAsync"] = _ => new CashDrawerStatusResponse
        {
            Shift = shiftOpen ? new CashShiftResponse { Id = Guid.NewGuid(), OpenedByName = "Ana", IsOpen = true } : null,
            SuggestedOpeningCash = 20_000,
        };
        var settings = DispatchProxy.Create<IPaymentSettingsClient, FakeClient>();
        ((FakeClient)(object)settings).Handlers["PaymentSettingsGETAsync"] = _ => new PaymentSettingsResponse { UseCashDrawer = useCashDrawer };
        Services.AddSingleton(cash);
        Services.AddSingleton(settings);
        Services.AddScoped<CashDrawerState>();
    }

    [Fact]
    public void Cash_with_the_drawer_closed_offers_to_open_it_inside_the_charge()
    {
        Setup();

        var cut = RenderComponent<CashOpenInline>(p => p.Add(c => c.IsCash, true));

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='cash-open-inline']")));
        Assert.Contains("La caja está cerrada", cut.Markup);
    }

    [Theory]
    [InlineData(false, false, true)]   // no es efectivo
    [InlineData(true, true, true)]     // la caja ya está abierta
    [InlineData(true, false, false)]   // el negocio no usa caja
    public void The_open_drawer_block_stays_hidden_when_it_does_not_apply(bool isCash, bool shiftOpen, bool useDrawer)
    {
        Setup(useDrawer, shiftOpen);

        var cut = RenderComponent<CashOpenInline>(p => p.Add(c => c.IsCash, isCash));

        Assert.Empty(cut.FindAll("[data-testid='cash-open-inline']"));
    }

    [Fact]
    public void Opening_the_drawer_from_the_charge_calls_the_open_endpoint_and_hides_the_block()
    {
        Setup();
        _cashFake.Handlers["OpenAsync"] = _ =>
        {
            _cashFake.Handlers["CurrentAsync"] = __ => new CashDrawerStatusResponse { Shift = new CashShiftResponse { Id = Guid.NewGuid(), IsOpen = true } };
            return new CashShiftResponse { Id = Guid.NewGuid(), IsOpen = true };
        };
        var cut = RenderComponent<CashOpenInline>(p => p.Add(c => c.IsCash, true));
        cut.WaitForAssertion(() => cut.Find("[data-testid='cash-open-inline-button']"));

        cut.Find("[data-testid='cash-open-inline-button']").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid='cash-open-inline']")));
        Assert.Contains(_cashFake.Calls, c => c.Method == "OpenAsync");
    }

    [Fact]
    public void The_info_tip_carries_its_text_for_screen_readers()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();

        var cut = RenderComponent<InfoTip>(p => p.Add(c => c.Text, "Detalle de la opción."));

        Assert.Equal("Detalle de la opción.", cut.Find("button.gsm-infotip").GetAttribute("aria-label"));
    }

    [Fact]
    public void The_info_tip_opens_on_click_and_stays_open_until_clicked_again()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();

        RenderComponent<MudBlazor.MudPopoverProvider>();
        var cut = RenderComponent<InfoTip>(p => p.Add(c => c.Text, "Detalle."));
        var button = cut.Find("button.gsm-infotip");
        Assert.Equal("false", button.GetAttribute("aria-expanded"));

        button.Click();
        Assert.Equal("true", cut.Find("button.gsm-infotip").GetAttribute("aria-expanded"));

        cut.Find("button.gsm-infotip").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
        Assert.Equal("false", cut.Find("button.gsm-infotip").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void A_warning_info_tip_says_so_in_its_label()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();

        var cut = RenderComponent<InfoTip>(p => p.Add(c => c.Text, "Cuidado.").Add(c => c.Warning, true));

        Assert.StartsWith("Aviso:", cut.Find("button.gsm-infotip").GetAttribute("aria-label"));
    }

    [Fact]
    public async Task The_charge_dialog_takes_the_pending_membership_amount_and_concept_on_its_own()
    {
        Setup();
        var memberId = Guid.NewGuid();
        var members = DispatchProxy.Create<IMembersClient, FakeClient>();
        ((FakeClient)(object)members).Handlers["MembersGET2Async"] = _ => new MemberDetail
        {
            Id = memberId, FirstName = "Ana", LastName = "Prueba", DocumentNumber = "123", Status = "Active",
        };
        var memberMemberships = DispatchProxy.Create<IMemberMembershipsClient, FakeClient>();
        _memberships = (FakeClient)(object)memberMemberships;
        _memberships.Handlers["MembershipsAllAsync"] = _ => new List<MembershipResponse>
        {
            new() { Id = Guid.NewGuid(), PlanName = "Mensual", Status = "Active", StartDate = new DateOnly(2026, 11, 1), EndDate = new DateOnly(2026, 11, 30), AmountDue = 80_000 },
        };
        Services.AddSingleton(members);
        Services.AddSingleton(memberMemberships);
        Services.AddSingleton(DispatchProxy.Create<IPaymentsClient, FakeClient>());

        RenderComponent<MudPopoverProvider>();
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<RecordPaymentDialog> { { x => x.MemberId, memberId } };
        await provider.InvokeAsync(() => dialogs.ShowAsync<RecordPaymentDialog>("Cobrar", parameters));

        provider.WaitForAssertion(() =>
        {
            Assert.Empty(provider.FindAll("[data-testid='no-pending-membership']"));
            Assert.Contains(provider.FindAll("input"), i => new string((i.GetAttribute("value") ?? string.Empty).Where(char.IsDigit).ToArray()).StartsWith("80000"));
        });
    }
}
