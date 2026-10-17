using System.Reflection;
using Bunit;
using Bunit.TestDoubles;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Attendance;
using GSM.Panel.UI.Pages.Members;
using GSM.Panel.UI.Pages.Plans;
using GSM.UI.Shared.Formatting;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using static GSM.Web.Tests.CommercialStatusUiTests;

namespace GSM.Web.Tests;

/// <summary>UI de MS5: tiqueteras en el formulario de planes, la asignación y la tarjeta de recepción.</summary>
public class PassUiTests : TestContext
{
    private static readonly MemberListItem Ana = new()
    {
        Id = Guid.NewGuid(), FirstName = "Ana", LastName = "Prueba", DocumentNumber = "1", DocumentType = "cc", Status = "Active",
    };

    [Theory]
    [InlineData("pass", "days", 30, 12, "12 entradas · 30 días")]
    [InlineData("pass", "days", 1, 1, "1 entrada · 1 día")]
    [InlineData("time", "months", 3, null, "3 meses")]
    [InlineData(null, "days", 30, null, "30 días")]
    public void Plan_label_shows_entries_and_validity_for_a_pass(string? kind, string unit, int value, int? entries, string expected)
        => Assert.Equal(expected, PlanDurationLabels.FormatPlan(kind, unit, value, entries));

    [Theory]
    [InlineData(4, 12, "Quedan 4 de 12 entradas")]
    [InlineData(1, 12, "Queda 1 de 12 entradas")]
    [InlineData(0, 12, "Quedan 0 de 12 entradas")]
    public void Balance_label_reads_naturally(int left, int total, string expected)
        => Assert.Equal(expected, PlanDurationLabels.Balance(left, total));

    [Fact]
    public async Task Editing_a_pass_plan_shows_entries_and_validity_and_saves_them_as_a_pass()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();

        var plan = new PlanResponse
        {
            Id = Guid.NewGuid(), Name = "Tiquetera 12", Price = 120000, DurationUnit = "days", DurationValue = 45, IsActive = true,
            Kind = "pass", Entries = 12, ValidityDays = 45,
        };
        var plans = System.Reflection.DispatchProxy.Create<IMembershipPlansClient, FakeClient>();
        var fake = (FakeClient)(object)plans;
        fake.Handlers["MembershipPlansGETAsync"] = _ => plan;
        fake.Handlers["MembershipPlansPUTAsync"] = _ => plan;
        Services.AddSingleton(plans);

        RenderComponent<MudPopoverProvider>();
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<PlanFormDialog> { { x => x.PlanId, plan.Id } };
        await provider.InvokeAsync(() => dialogs.ShowAsync<PlanFormDialog>("Plan", parameters));

        provider.WaitForAssertion(() =>
        {
            Assert.NotNull(provider.Find("#plan-entries"));
            Assert.NotNull(provider.Find("#plan-validity"));
            Assert.Contains("las entradas que sobren al vencer se pierden", provider.Find("#pass-help").TextContent);
        });
        // En una tiquetera no hay duración en días o meses del calendario.
        Assert.DoesNotContain("Meses del calendario", provider.Markup);

        provider.FindAll("button").First(b => b.TextContent.Contains("Guardar")).Click();

        provider.WaitForAssertion(() =>
        {
            var call = Assert.Single(fake.Calls, c => c.Method == "MembershipPlansPUTAsync");
            var request = (UpdatePlanRequest)call.Args[1]!;
            Assert.Equal("pass", request.Kind);
            Assert.Equal(12, request.Entries);
            Assert.Equal(45, request.ValidityDays);
            Assert.Equal(45, request.DurationValue);
            Assert.Equal("days", request.DurationUnit);
        });
    }

    [Fact]
    public async Task A_new_plan_form_starts_with_thirty_freeze_days()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var plans = System.Reflection.DispatchProxy.Create<IMembershipPlansClient, FakeClient>();
        Services.AddSingleton(plans);

        RenderComponent<MudPopoverProvider>();
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        await provider.InvokeAsync(() => dialogs.ShowAsync<PlanFormDialog>("Plan"));

        provider.WaitForAssertion(() =>
            Assert.Equal("30", provider.Find("input#plan-max-freeze").GetAttribute("value")));
    }

    [Fact]
    public async Task Editing_a_time_plan_does_not_send_entries()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();

        var plan = new PlanResponse
        {
            Id = Guid.NewGuid(), Name = "Mensual", Price = 80000, DurationUnit = "days", DurationValue = 30, IsActive = true, Kind = "time",
        };
        var plans = System.Reflection.DispatchProxy.Create<IMembershipPlansClient, FakeClient>();
        var fake = (FakeClient)(object)plans;
        fake.Handlers["MembershipPlansGETAsync"] = _ => plan;
        fake.Handlers["MembershipPlansPUTAsync"] = _ => plan;
        Services.AddSingleton(plans);

        RenderComponent<MudPopoverProvider>();
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<PlanFormDialog> { { x => x.PlanId, plan.Id } };
        await provider.InvokeAsync(() => dialogs.ShowAsync<PlanFormDialog>("Plan", parameters));

        provider.WaitForAssertion(() => Assert.Contains("Unidad", provider.Markup));
        Assert.Empty(provider.FindAll("#plan-entries"));

        provider.FindAll("button").First(b => b.TextContent.Contains("Guardar")).Click();

        provider.WaitForAssertion(() =>
        {
            var request = (UpdatePlanRequest)Assert.Single(fake.Calls, c => c.Method == "MembershipPlansPUTAsync").Args[1]!;
            Assert.Equal("time", request.Kind);
            Assert.Null(request.Entries);
            Assert.Null(request.ValidityDays);
        });
    }

    [Fact]
    public async Task Assigning_a_pass_lists_its_entries_and_hides_the_start_date()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();

        var plan = new PlanResponse
        {
            Id = Guid.NewGuid(), Name = "Tiquetera 12", Price = 120000, DurationUnit = "days", DurationValue = 30, IsActive = true,
            Kind = "pass", Entries = 12, ValidityDays = 30,
        };
        var plans = System.Reflection.DispatchProxy.Create<IMembershipPlansClient, FakeClient>();
        ((FakeClient)(object)plans).Handlers["MembershipPlansAllAsync"] = _ => new List<PlanResponse> { plan };
        var memberships = System.Reflection.DispatchProxy.Create<IMemberMembershipsClient, FakeClient>();
        var membershipsFake = (FakeClient)(object)memberships;
        membershipsFake.Handlers["MembershipsAllAsync"] = _ => new List<MembershipResponse>();
        membershipsFake.Handlers["PreviewAsync"] = _ => new RenewalPreviewResponse
        {
            Kind = "new", SystemStartDate = new DateOnly(2026, 10, 12), StartDate = new DateOnly(2026, 10, 12),
            EndDate = new DateOnly(2026, 11, 10), StartDateChanged = false, EnrollmentFee = 0, PlanPrice = 120000,
        };
        Services.AddSingleton(plans);
        Services.AddSingleton(memberships);
        Services.AddSingleton(System.Reflection.DispatchProxy.Create<IPaymentsClient, FakeClient>());

        RenderComponent<MudPopoverProvider>();
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<AssignMembershipDialog> { { x => x.MemberId, Guid.NewGuid() } };
        await provider.InvokeAsync(() => dialogs.ShowAsync<AssignMembershipDialog>("Asignar", parameters));

        provider.WaitForAssertion(() =>
        {
            Assert.Contains("12 entradas · 30 días", provider.Markup);
            Assert.Contains("siempre empieza el día en que se compra", provider.Find("#pass-start-note").TextContent);
        });
        Assert.DoesNotContain("Cambiar la fecha de inicio", provider.Markup);
    }

    [Fact]
    public async Task Reception_card_shows_the_pass_balance()
    {
        var page = Render(out _, new ReceptionCardResponse
        {
            MemberId = Ana.Id, MemberName = "Ana Prueba", MemberActive = true, Commercial = "current", Kind = "covered",
            DaysLeft = 20, CanRegister = true, EntriesLeft = 4, EntriesTotal = 12,
        });

        await Pick(page);

        page.WaitForAssertion(() => Assert.Equal("Quedan 4 de 12 entradas", page.Find("#pass-balance").TextContent.Trim()));
    }

    [Fact]
    public async Task Reception_card_with_an_exhausted_pass_offers_to_let_the_member_through()
    {
        var page = Render(out var fake, new ReceptionCardResponse
        {
            MemberId = Ana.Id, MemberName = "Ana Prueba", MemberActive = true, Commercial = "expired", Kind = "blocked",
            BlockMessage = "Ya usó todas las entradas de su tiquetera.", CanRegister = false, CanLetThrough = true,
            RejectionReason = "pass_exhausted", EntriesLeft = 0, EntriesTotal = 12,
        });

        await Pick(page);

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Ya usó todas las entradas de su tiquetera.", page.Markup);
            Assert.Contains("Quedan 0 de 12 entradas", page.Find("#pass-balance").TextContent);
            Assert.Contains("Dejar pasar", page.Markup);
        });
        Assert.DoesNotContain(fake.Calls, c => c.Method == "CheckinAsync");
    }

    [Fact]
    public async Task Registering_a_pass_entry_shows_what_is_left_after_it()
    {
        var page = Render(out var fake, new ReceptionCardResponse
        {
            MemberId = Ana.Id, MemberName = "Ana Prueba", MemberActive = true, Commercial = "current", Kind = "covered",
            CanRegister = true, EntriesLeft = 3, EntriesTotal = 12,
        });
        fake.Handlers["CheckinAsync"] = _ => new CheckInResponse
        {
            AttendanceId = Guid.NewGuid(), MemberId = Ana.Id, MemberName = "Ana Prueba", Method = "Manual",
            CheckedInAt = DateTimeOffset.UtcNow, EntriesLeft = 2, EntriesTotal = 12,
            Notice = "Te quedan 2 entradas de 12 (vence el 30 nov).",
        };

        await Pick(page);
        page.WaitForAssertion(() => page.Find("#register-button").Click());

        page.WaitForAssertion(() =>
        {
            Assert.Equal("Quedan 2 de 12 entradas", page.Find("#pass-balance-result").TextContent.Trim());
            Assert.Contains("Te quedan 2 entradas de 12", page.Find("#access-notice").TextContent);
        });
    }

    private IRenderedComponent<CheckIn> Render(out FakeClient attendanceFake, ReceptionCardResponse card)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var proxy = DispatchProxy.Create<IAttendanceClient, FakeClient>();
        attendanceFake = (FakeClient)(object)proxy;
        attendanceFake.Handlers["ReceptionCardAsync"] = _ => card;
        Services.AddSingleton(proxy);
        Services.AddSingleton(DispatchProxy.Create<IMembersClient, FakeClient>());
        Services.AddSingleton(DispatchProxy.Create<IMemberMembershipsClient, FakeClient>());
        Services.AddSingleton(DispatchProxy.Create<IPaymentsClient, FakeClient>());
        Services.AddSingleton(DispatchProxy.Create<IMembershipPlansClient, FakeClient>());
        var whatsApp = DispatchProxy.Create<IWhatsAppClient, FakeClient>();
        ((FakeClient)(object)whatsApp).Handlers["StatesAsync"] = _ => new List<WhatsAppMemberStateResponse>();
        Services.AddSingleton(whatsApp);

        this.AddTestAuthorization().SetAuthorized("staff");
        RenderComponent<MudPopoverProvider>();
        RenderComponent<MudDialogProvider>();
        return RenderComponent<CheckIn>();
    }

    private static async Task Pick(IRenderedComponent<CheckIn> page)
    {
        await page.InvokeAsync(() => (Task)typeof(CheckIn)
            .GetMethod("OnMemberPickedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(page.Instance, [Ana])!);
        page.Render();
    }
}
