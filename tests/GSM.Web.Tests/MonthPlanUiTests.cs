using Bunit;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Members;
using GSM.Panel.UI.Pages.Plans;
using GSM.UI.Shared.Formatting;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>UI de MS3: la duración de un plan se edita y se muestra en días o en meses del calendario.</summary>
public class MonthPlanUiTests : TestContext
{
    [Theory]
    [InlineData("months", 1, "1 mes")]
    [InlineData("months", 3, "3 meses")]
    [InlineData("days", 30, "30 días")]
    [InlineData("days", 1, "1 día")]
    [InlineData(null, 15, "15 días")]
    public void Duration_label_uses_the_plan_unit(string? unit, int value, string expected)
        => Assert.Equal(expected, PlanDurationLabels.Format(unit, value));

    [Fact]
    public async Task Editing_a_month_plan_shows_the_unit_and_saves_it_as_months()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();

        var plan = new PlanResponse
        {
            Id = Guid.NewGuid(), Name = "Trimestral", Price = 200000, DurationUnit = "months", DurationValue = 3, IsActive = true,
        };
        var plans = System.Reflection.DispatchProxy.Create<IMembershipPlansClient, CommercialStatusUiTests.FakeClient>();
        var fake = (CommercialStatusUiTests.FakeClient)(object)plans;
        fake.Handlers["MembershipPlansGETAsync"] = _ => plan;
        fake.Handlers["MembershipPlansPUTAsync"] = _ => plan;
        Services.AddSingleton(plans);

        RenderComponent<MudPopoverProvider>();
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<PlanFormDialog> { { x => x.PlanId, plan.Id } };
        await provider.InvokeAsync(() => dialogs.ShowAsync<PlanFormDialog>("Plan", parameters));

        provider.WaitForAssertion(() => Assert.Contains("Meses del calendario", provider.Markup));
        Assert.Contains("cubre hasta el 7 de octubre", provider.Markup);

        provider.FindAll("button").First(b => b.TextContent.Contains("Guardar")).Click();

        provider.WaitForAssertion(() =>
        {
            var call = Assert.Single(fake.Calls, c => c.Method == "MembershipPlansPUTAsync");
            var request = (UpdatePlanRequest)call.Args[1]!;
            Assert.Equal("months", request.DurationUnit);
            Assert.Equal(3, request.DurationValue);
        });
    }

    [Fact]
    public async Task Assign_dialog_lists_month_plans_with_their_duration()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();

        var plan = new PlanResponse
        {
            Id = Guid.NewGuid(), Name = "Trimestral", Price = 200000, DurationUnit = "months", DurationValue = 3, IsActive = true,
        };
        var memberId = Guid.NewGuid();
        var plans = System.Reflection.DispatchProxy.Create<IMembershipPlansClient, CommercialStatusUiTests.FakeClient>();
        ((CommercialStatusUiTests.FakeClient)(object)plans).Handlers["MembershipPlansAllAsync"] = _ => new List<PlanResponse> { plan };
        var memberships = System.Reflection.DispatchProxy.Create<IMemberMembershipsClient, CommercialStatusUiTests.FakeClient>();
        var membershipsFake = (CommercialStatusUiTests.FakeClient)(object)memberships;
        membershipsFake.Handlers["MembershipsAllAsync"] = _ => new List<MembershipResponse>();
        membershipsFake.Handlers["PreviewAsync"] = _ => new RenewalPreviewResponse
        {
            Kind = "new",
            SystemStartDate = new DateOnly(2026, 9, 8),
            StartDate = new DateOnly(2026, 9, 8),
            EndDate = new DateOnly(2026, 12, 7),
            StartDateChanged = false,
            EnrollmentFee = 0,
            PlanPrice = 200000,
        };
        Services.AddSingleton(plans);
        Services.AddSingleton(memberships);
        Services.AddSingleton(System.Reflection.DispatchProxy.Create<IPaymentsClient, CommercialStatusUiTests.FakeClient>());

        RenderComponent<MudPopoverProvider>();
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<AssignMembershipDialog> { { x => x.MemberId, memberId } };
        await provider.InvokeAsync(() => dialogs.ShowAsync<AssignMembershipDialog>("Asignar", parameters));

        provider.WaitForAssertion(() => Assert.Contains("3 meses", provider.Markup));
        Assert.DoesNotContain("90 días", provider.Markup);
    }
}
