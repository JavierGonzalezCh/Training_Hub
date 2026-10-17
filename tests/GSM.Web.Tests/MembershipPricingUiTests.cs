using AngleSharp.Dom;
using Bunit;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Members;
using GSM.Panel.UI.Pages.Settings;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>UI de MS2: el diálogo de asignar muestra precio acordado, descuento, matrícula y total; el cobro es parte de asignar.</summary>
public class MembershipPricingUiTests : TestContext
{
    private static readonly DateOnly Start = new(2026, 10, 8);
    private static readonly DateOnly End = new(2026, 11, 6);

    private readonly PlanResponse _plan = new() { Id = Guid.NewGuid(), Name = "Mensual", Price = 80000, DurationUnit = "days", DurationValue = 30, IsActive = true };
    private readonly Guid _memberId = Guid.NewGuid();
    private CommercialStatusUiTests.FakeClient _membershipsFake = null!;
    private CommercialStatusUiTests.FakeClient _paymentsFake = null!;
    private IRenderedComponent<MudDialogProvider> _provider = null!;

    private async Task OpenAsync(double enrollmentFee)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();

        var plans = System.Reflection.DispatchProxy.Create<IMembershipPlansClient, CommercialStatusUiTests.FakeClient>();
        ((CommercialStatusUiTests.FakeClient)(object)plans).Handlers["MembershipPlansAllAsync"] = _ => new List<PlanResponse> { _plan };
        var memberships = System.Reflection.DispatchProxy.Create<IMemberMembershipsClient, CommercialStatusUiTests.FakeClient>();
        _membershipsFake = (CommercialStatusUiTests.FakeClient)(object)memberships;
        _membershipsFake.Handlers["MembershipsAllAsync"] = _ => new List<MembershipResponse>();
        _membershipsFake.Handlers["PreviewAsync"] = _ => new RenewalPreviewResponse
        {
            Kind = "new", SystemStartDate = Start, StartDate = Start, EndDate = End, StartDateChanged = false,
            EnrollmentFee = enrollmentFee, PlanPrice = 80000,
        };
        _membershipsFake.Handlers["MembershipsAsync"] = args =>
        {
            var request = (AssignMembershipRequest)args[1]!;
            var agreed = request.AgreedPrice ?? 80000;
            var fee = request.WaiveEnrollment == true ? 0 : enrollmentFee;
            return new MembershipResponse
            {
                Id = Guid.NewGuid(), MemberId = _memberId, PlanId = _plan.Id, PlanName = "Mensual", Status = "Active",
                StartDate = Start, EndDate = End, AgreedPrice = agreed, EnrollmentFee = fee, TotalToCharge = agreed + fee,
            };
        };
        var payments = System.Reflection.DispatchProxy.Create<IPaymentsClient, CommercialStatusUiTests.FakeClient>();
        _paymentsFake = (CommercialStatusUiTests.FakeClient)(object)payments;
        Services.AddSingleton(plans);
        Services.AddSingleton(memberships);
        Services.AddSingleton(payments);

        RenderComponent<MudPopoverProvider>();
        _provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<AssignMembershipDialog> { { x => x.MemberId, _memberId } };
        await _provider.InvokeAsync(() => dialogs.ShowAsync<AssignMembershipDialog>("Asignar", parameters));
        _provider.WaitForAssertion(() => Assert.Contains("Total a cobrar", _provider.Markup));
    }

    private IElement Input(string id)
    {
        var inner = _provider.FindAll($"#{id} input");
        return inner.Count > 0 ? inner[0] : _provider.Find($"#{id}");
    }

    private void Click(string text)
        => _provider.FindAll("button").First(b => b.TextContent.Contains(text)).Click();

    [Fact]
    public async Task The_dialog_has_no_optional_charge_and_shows_the_plan_price_and_enrollment_in_the_total()
    {
        await OpenAsync(enrollmentFee: 30000);

        Assert.DoesNotContain("Cobrar ahora", _provider.Markup);
        Assert.DoesNotContain("sin pago registrado", _provider.Markup);
        Assert.Contains("Matrícula", _provider.Markup);
        Assert.Contains("Asignar y cobrar", _provider.Markup);
        var total = _provider.Find("#total-to-charge").TextContent;
        Assert.Contains("110", total);
    }

    [Fact]
    public async Task Without_enrollment_there_is_no_waive_option()
    {
        await OpenAsync(enrollmentFee: 0);

        Assert.Empty(_provider.FindAll("#waive-enrollment"));
        Assert.Contains("80", _provider.Find("#total-to-charge").TextContent);
    }

    [Fact]
    public async Task Lowering_the_price_asks_for_a_discount_reason_before_assigning()
    {
        await OpenAsync(enrollmentFee: 0);
        Assert.Empty(_provider.FindAll("#discount-reason"));

        Input("agreed-price").Change("60000");
        _provider.WaitForAssertion(() => Assert.NotEmpty(_provider.FindAll("#discount-reason")));

        // Sin motivo no se llama al servidor.
        Click("Asignar y cobrar");
        _provider.WaitForAssertion(() => Assert.Contains("motivo del descuento", _provider.Markup));
        Assert.DoesNotContain(_membershipsFake.Calls, c => c.Method == "MembershipsAsync");

        Input("discount-reason").Change("Convenio con la empresa");
        Click("Asignar y cobrar");

        _provider.WaitForAssertion(() => Assert.Contains(_membershipsFake.Calls, c => c.Method == "MembershipsAsync"));
        var request = (AssignMembershipRequest)_membershipsFake.Calls.Last(c => c.Method == "MembershipsAsync").Args[1]!;
        Assert.Equal(60000, request.AgreedPrice);
        Assert.Equal("Convenio con la empresa", request.DiscountReason);
    }

    [Fact]
    public async Task Waiving_the_enrollment_asks_for_a_reason_and_is_sent_with_the_request()
    {
        await OpenAsync(enrollmentFee: 30000);

        Input("waive-enrollment").Change(true);
        _provider.WaitForAssertion(() => Assert.NotEmpty(_provider.FindAll("#waive-reason")));
        Assert.Contains("80", _provider.Find("#total-to-charge").TextContent);
        Assert.Contains("exonerada", _provider.Find("#total-to-charge").TextContent);

        Click("Asignar y cobrar");
        _provider.WaitForAssertion(() => Assert.Contains("motivo para exonerar", _provider.Markup));
        Assert.DoesNotContain(_membershipsFake.Calls, c => c.Method == "MembershipsAsync");

        Input("waive-reason").Change("Promoción de apertura");
        Click("Asignar y cobrar");

        _provider.WaitForAssertion(() => Assert.Contains(_membershipsFake.Calls, c => c.Method == "MembershipsAsync"));
        var request = (AssignMembershipRequest)_membershipsFake.Calls.Last(c => c.Method == "MembershipsAsync").Args[1]!;
        Assert.True(request.WaiveEnrollment);
        Assert.Equal("Promoción de apertura", request.EnrollmentWaivedReason);
    }

    [Fact]
    public async Task Assigning_charges_the_full_total_in_a_single_payment_linked_to_the_membership()
    {
        await OpenAsync(enrollmentFee: 30000);
        _paymentsFake.Handlers["PaymentsPOST2Async"] = _ => new PaymentResponse();

        Click("Asignar y cobrar");

        _provider.WaitForAssertion(() => Assert.Contains(_paymentsFake.Calls, c => c.Method == "PaymentsPOST2Async"));
        var payment = (RecordPaymentRequest)_paymentsFake.Calls.Single(c => c.Method == "PaymentsPOST2Async").Args[0]!;
        Assert.Equal(110000, payment.Amount);
        Assert.Equal(_memberId, payment.MemberId);
        Assert.NotNull(payment.MembershipId);
        Assert.Contains("matrícula", payment.Concept);
    }

    [Fact]
    public async Task A_total_of_zero_assigns_without_creating_a_payment()
    {
        await OpenAsync(enrollmentFee: 0);

        Input("agreed-price").Change("0");
        _provider.WaitForAssertion(() => Assert.Contains("Asignar", _provider.FindAll("button").Last().TextContent));
        Assert.DoesNotContain("Asignar y cobrar", _provider.Markup);
        Input("discount-reason").Change("Cortesía del gerente");
        Click("Asignar");

        _provider.WaitForAssertion(() => Assert.Contains(_membershipsFake.Calls, c => c.Method == "MembershipsAsync"));
        Assert.DoesNotContain(_paymentsFake.Calls, c => c.Method == "PaymentsPOST2Async");
    }

    [Fact]
    public async Task When_the_charge_fails_the_dialog_stays_open_and_offers_retry_or_cancelling_the_membership()
    {
        await OpenAsync(enrollmentFee: 0);
        _paymentsFake.Handlers["PaymentsPOST2Async"] = _ => throw new GsmApiException("Conflict", 409, "{\"detail\":\"Caja cerrada\"}", new Dictionary<string, IEnumerable<string>>(), null);

        Click("Asignar y cobrar");

        _provider.WaitForAssertion(() => Assert.NotEmpty(_provider.FindAll("#charge-failed")));
        Assert.Contains("Reintentar el cobro", _provider.Markup);
        Assert.Contains("Cancelar la membresía", _provider.Markup);

        // Cancelar la membresía recién creada, con motivo.
        Click("Cancelar la membresía");
        _provider.WaitForAssertion(() => Assert.Contains(_membershipsFake.Calls, c => c.Method == "Cancel2Async"));
        var cancel = (CancelMembershipRequest)_membershipsFake.Calls.Last(c => c.Method == "Cancel2Async").Args[2]!;
        Assert.False(string.IsNullOrWhiteSpace(cancel.Reason));
    }

    [Fact]
    public void The_memberships_settings_tab_edits_the_enrollment_fee_grace_and_expiring_days()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();

        var settings = System.Reflection.DispatchProxy.Create<IMembershipSettingsClient, CommercialStatusUiTests.FakeClient>();
        var settingsFake = (CommercialStatusUiTests.FakeClient)(object)settings;
        settingsFake.Handlers["MembershipSettingsGETAsync"] = _ => new MembershipSettingsResponse { GraceDays = 3, ExpiringDays = 7, EnrollmentFee = 25000 };
        settingsFake.Handlers["MembershipSettingsPUTAsync"] = args =>
        {
            var put = (UpdateMembershipSettingsRequest)args[0]!;
            return new MembershipSettingsResponse { GraceDays = put.GraceDays, ExpiringDays = put.ExpiringDays, EnrollmentFee = put.EnrollmentFee };
        };
        Services.AddSingleton(settings);

        RenderComponent<MudPopoverProvider>();
        var tab = RenderComponent<MembershipSettingsTab>();

        tab.WaitForAssertion(() => Assert.Contains("Matrícula (COP)", tab.Markup));
        Assert.NotEmpty(tab.FindAll("#enrollment-fee"));
        Assert.Equal("3", tab.Find("input#grace-days").GetAttribute("value"));
        Assert.Equal("7", tab.Find("input#expiring-days").GetAttribute("value"));

        tab.Find("input#grace-days").Change("5");
        tab.Find("#membership-settings-save").Click();

        tab.WaitForAssertion(() =>
        {
            var put = Assert.Single(settingsFake.Calls, c => c.Method == "MembershipSettingsPUTAsync");
            Assert.Equal(5, Assert.IsType<UpdateMembershipSettingsRequest>(put.Args[0]).GraceDays);
        });
    }

    [Fact]
    public void The_checkin_tab_no_longer_has_the_membership_settings()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();

        var attendance = System.Reflection.DispatchProxy.Create<IAttendanceClient, CommercialStatusUiTests.FakeClient>();
        ((CommercialStatusUiTests.FakeClient)(object)attendance).Handlers["SettingsGETAsync"] = _ => new CheckinSettingsResponse { Mode = "Rotating" };
        Services.AddSingleton(attendance);

        RenderComponent<MudPopoverProvider>();
        var tab = RenderComponent<CheckinTab>();

        tab.WaitForAssertion(() => Assert.Contains("Tipo de QR en la entrada", tab.Markup));
        Assert.Empty(tab.FindAll("#enrollment-fee"));
        Assert.DoesNotContain("Días de gracia", tab.Markup);
        Assert.DoesNotContain("Matrícula", tab.Markup);
    }

    [Fact]
    public void The_checkin_poster_is_offered_only_in_static_mode_and_shows_the_fixed_code()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();

        var attendance = System.Reflection.DispatchProxy.Create<IAttendanceClient, CommercialStatusUiTests.FakeClient>();
        var fake = (CommercialStatusUiTests.FakeClient)(object)attendance;
        fake.Handlers["SettingsGETAsync"] = _ => new CheckinSettingsResponse { Mode = "Static" };
        fake.Handlers["GetStaticCodeAsync"] = _ => new KioskCodeResponse { Mode = "Static", Code = "s1.fixedcode" };
        Services.AddSingleton(attendance);
        Services.AddSingleton<GSM.UI.Shared.Auth.GymContext>();

        var provider = RenderComponent<MudDialogProvider>();
        RenderComponent<MudPopoverProvider>();
        var tab = RenderComponent<CheckinTab>();

        tab.WaitForAssertion(() => Assert.NotEmpty(tab.FindAll("[data-testid='show-qr-poster']")));
        tab.Find("[data-testid='show-qr-poster']").Click();

        provider.WaitForAssertion(() => Assert.NotEmpty(provider.FindAll("[data-testid='qr-poster-preview']")));
        Assert.Contains("Escanea para registrar tu entrada", provider.Markup);
        Assert.NotEmpty(provider.FindAll("[data-testid='qr-poster-print']"));
    }

    [Fact]
    public void The_checkin_poster_button_is_hidden_in_rotating_mode()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();

        var attendance = System.Reflection.DispatchProxy.Create<IAttendanceClient, CommercialStatusUiTests.FakeClient>();
        ((CommercialStatusUiTests.FakeClient)(object)attendance).Handlers["SettingsGETAsync"] = _ => new CheckinSettingsResponse { Mode = "Rotating" };
        Services.AddSingleton(attendance);

        RenderComponent<MudPopoverProvider>();
        var tab = RenderComponent<CheckinTab>();

        tab.WaitForAssertion(() => Assert.Contains("Tipo de QR en la entrada", tab.Markup));
        Assert.Empty(tab.FindAll("[data-testid='show-qr-poster']"));
    }

    [Fact]
    public void The_modules_register_the_memberships_routines_and_equipment_settings_sections_for_the_owner()
    {
        var sections = new GSM.Panel.UI.Navigation.IPanelModule[]
        {
            new GSM.Panel.UI.Navigation.Modules.MembershipsPanelModule(),
            new GSM.Panel.UI.Navigation.Modules.RoutinesPanelModule(),
            new GSM.Panel.UI.Navigation.Modules.EquipmentPanelModule(),
        }.SelectMany(m => m.SettingsSections).ToList();

        Assert.Contains(sections, s => s.Component == typeof(MembershipSettingsTab) && s.Roles == "owner" && s.Group == "Membresías");
        Assert.Contains(sections, s => s.Component == typeof(RoutineSettingsTab) && s.Roles == "owner");
        Assert.Contains(sections, s => s.Component == typeof(EquipmentSettingsTab) && s.Roles == "owner" && s.Group == "Equipos");
    }
}
