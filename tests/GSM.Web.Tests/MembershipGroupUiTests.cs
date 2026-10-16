using Bunit;
using Bunit.TestDoubles;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Members;
using GSM.Panel.UI.Pages.Plans;
using GSM.UI.Shared.Formatting;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using static GSM.Web.Tests.CommercialStatusUiTests;

namespace GSM.Web.Tests;

/// <summary>UI de MS7: planes de grupo en el formulario de planes, la asignación y el diálogo del grupo.</summary>
public class MembershipGroupUiTests : TestContext
{
    private static readonly DateOnly Start = new(2026, 10, 8);
    private static readonly DateOnly End = new(2026, 11, 6);

    [Theory]
    [InlineData(1, "")]
    [InlineData(2, "Grupo de 2")]
    [InlineData(4, "Grupo de 4")]
    public void Seats_label_only_shows_for_a_group(int seats, string expected)
        => Assert.Equal(expected, PlanDurationLabels.SeatsLabel(seats));

    [Fact]
    public async Task Editing_a_group_plan_loads_and_saves_its_seats()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();

        var plan = new PlanResponse
        {
            Id = Guid.NewGuid(), Name = "Familiar", Price = 200000, DurationUnit = "days", DurationValue = 30, IsActive = true,
            Kind = "time", Seats = 4,
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

        provider.WaitForAssertion(() => Assert.NotNull(provider.Find("#plan-seats")));

        provider.FindAll("button").First(b => b.TextContent.Contains("Guardar")).Click();

        provider.WaitForAssertion(() =>
        {
            var request = (UpdatePlanRequest)Assert.Single(fake.Calls, c => c.Method == "MembershipPlansPUTAsync").Args[1]!;
            Assert.Equal(4, request.Seats);
        });
    }

    [Fact]
    public async Task A_pass_plan_does_not_offer_seats()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();

        var plan = new PlanResponse
        {
            Id = Guid.NewGuid(), Name = "Tiquetera 12", Price = 120000, DurationUnit = "days", DurationValue = 45, IsActive = true,
            Kind = "pass", Entries = 12, ValidityDays = 45, Seats = 1,
        };
        var plans = System.Reflection.DispatchProxy.Create<IMembershipPlansClient, FakeClient>();
        ((FakeClient)(object)plans).Handlers["MembershipPlansGETAsync"] = _ => plan;
        Services.AddSingleton(plans);

        RenderComponent<MudPopoverProvider>();
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<PlanFormDialog> { { x => x.PlanId, plan.Id } };
        await provider.InvokeAsync(() => dialogs.ShowAsync<PlanFormDialog>("Plan", parameters));

        provider.WaitForAssertion(() => Assert.NotNull(provider.Find("#plan-entries")));
        Assert.Empty(provider.FindAll("#plan-seats"));
    }

    [Fact]
    public async Task The_individual_assign_dialog_does_not_list_group_plans()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();

        var monthly = new PlanResponse { Id = Guid.NewGuid(), Name = "Mensual", Price = 50000, DurationUnit = "days", DurationValue = 30, IsActive = true, Seats = 1 };
        var family = new PlanResponse { Id = Guid.NewGuid(), Name = "Familiar", Price = 150000, DurationUnit = "days", DurationValue = 30, IsActive = true, Seats = 4 };
        var plans = System.Reflection.DispatchProxy.Create<IMembershipPlansClient, FakeClient>();
        ((FakeClient)(object)plans).Handlers["MembershipPlansAllAsync"] = _ => new List<PlanResponse> { monthly, family };
        var memberships = System.Reflection.DispatchProxy.Create<IMemberMembershipsClient, FakeClient>();
        var membershipsFake = (FakeClient)(object)memberships;
        membershipsFake.Handlers["MembershipsAllAsync"] = _ => new List<MembershipResponse>();
        membershipsFake.Handlers["PreviewAsync"] = _ => new RenewalPreviewResponse
        {
            Kind = "new", SystemStartDate = Start, StartDate = Start, EndDate = End, StartDateChanged = false, EnrollmentFee = 0, PlanPrice = 50000,
        };
        Services.AddSingleton(plans);
        Services.AddSingleton(memberships);
        Services.AddSingleton(System.Reflection.DispatchProxy.Create<IPaymentsClient, FakeClient>());

        RenderComponent<MudPopoverProvider>();
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<AssignMembershipDialog> { { x => x.MemberId, Guid.NewGuid() } };
        await provider.InvokeAsync(() => dialogs.ShowAsync<AssignMembershipDialog>("Asignar", parameters));

        provider.WaitForAssertion(() => Assert.Contains("Mensual", provider.Markup));
        Assert.DoesNotContain("Familiar", provider.Markup);
    }

    [Fact]
    public async Task Assigning_a_group_creates_it_for_the_holder_and_charges_the_holder_membership()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();

        var holderId = Guid.NewGuid();
        var holderMembershipId = Guid.NewGuid();
        var family = new PlanResponse { Id = Guid.NewGuid(), Name = "Familiar", Price = 150000, DurationUnit = "days", DurationValue = 30, IsActive = true, Seats = 4 };
        var plans = System.Reflection.DispatchProxy.Create<IMembershipPlansClient, FakeClient>();
        ((FakeClient)(object)plans).Handlers["MembershipPlansAllAsync"] = _ => new List<PlanResponse> { family };
        var memberships = System.Reflection.DispatchProxy.Create<IMemberMembershipsClient, FakeClient>();
        ((FakeClient)(object)memberships).Handlers["PreviewAsync"] = _ => new RenewalPreviewResponse
        {
            Kind = "new", SystemStartDate = Start, StartDate = Start, EndDate = End, StartDateChanged = false, EnrollmentFee = 0, PlanPrice = 150000,
        };
        var groups = System.Reflection.DispatchProxy.Create<IMembershipGroupsClient, FakeClient>();
        var groupsFake = (FakeClient)(object)groups;
        groupsFake.Handlers["AssignGroupMembershipAsync"] = _ => new MembershipGroupResponse
        {
            Id = Guid.NewGuid(), PlanId = family.Id, PlanName = "Familiar", Seats = 4, SeatsUsed = 1, SeatsFree = 3,
            HolderMemberId = holderId, HolderMembershipId = holderMembershipId, StartDate = Start, EndDate = End,
            TotalToCharge = 150000, Members = new List<MembershipGroupMemberResponse>(),
        };
        var payments = System.Reflection.DispatchProxy.Create<IPaymentsClient, FakeClient>();
        var paymentsFake = (FakeClient)(object)payments;
        paymentsFake.Handlers["PaymentsPOST2Async"] = _ => new PaymentResponse();
        Services.AddSingleton(plans);
        Services.AddSingleton(memberships);
        Services.AddSingleton(groups);
        Services.AddSingleton(payments);
        Services.AddSingleton(System.Reflection.DispatchProxy.Create<IMembersClient, FakeClient>());

        RenderComponent<MudPopoverProvider>();
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<AssignGroupMembershipDialog> { { x => x.MemberId, holderId } };
        await provider.InvokeAsync(() => dialogs.ShowAsync<AssignGroupMembershipDialog>("Plan de grupo", parameters));

        provider.WaitForAssertion(() =>
        {
            Assert.Contains("Grupo de 4", provider.Markup);
            Assert.Contains("Empieza el", provider.Find("#group-preview").TextContent);
            Assert.Contains("Total a cobrar al titular", provider.Find("#group-total-to-charge").TextContent);
        });

        provider.FindAll("button").First(b => b.TextContent.Contains("Asignar y cobrar")).Click();

        provider.WaitForAssertion(() =>
        {
            var assign = (AssignGroupMembershipRequest)Assert.Single(groupsFake.Calls, c => c.Method == "AssignGroupMembershipAsync").Args[0]!;
            Assert.Equal(holderId, assign.HolderMemberId);
            Assert.Equal(family.Id, assign.PlanId);
            Assert.Empty(assign.BeneficiaryMemberIds);

            var charge = (RecordPaymentRequest)Assert.Single(paymentsFake.Calls, c => c.Method == "PaymentsPOST2Async").Args[0]!;
            Assert.Equal(holderMembershipId, charge.MembershipId);
            Assert.Equal(holderId, charge.MemberId);
            Assert.Equal(150000, charge.Amount);
        });
    }

    [Fact]
    public async Task Without_group_plans_the_group_dialog_says_to_create_one()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();

        var individual = new PlanResponse { Id = Guid.NewGuid(), Name = "Mensual", Price = 50000, DurationUnit = "days", DurationValue = 30, IsActive = true, Seats = 1 };
        var plans = System.Reflection.DispatchProxy.Create<IMembershipPlansClient, FakeClient>();
        ((FakeClient)(object)plans).Handlers["MembershipPlansAllAsync"] = _ => new List<PlanResponse> { individual };
        Services.AddSingleton(plans);
        Services.AddSingleton(System.Reflection.DispatchProxy.Create<IMemberMembershipsClient, FakeClient>());
        Services.AddSingleton(System.Reflection.DispatchProxy.Create<IMembershipGroupsClient, FakeClient>());
        Services.AddSingleton(System.Reflection.DispatchProxy.Create<IPaymentsClient, FakeClient>());
        Services.AddSingleton(System.Reflection.DispatchProxy.Create<IMembersClient, FakeClient>());

        RenderComponent<MudPopoverProvider>();
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<AssignGroupMembershipDialog> { { x => x.MemberId, Guid.NewGuid() } };
        await provider.InvokeAsync(() => dialogs.ShowAsync<AssignGroupMembershipDialog>("Plan de grupo", parameters));

        provider.WaitForAssertion(() => Assert.Contains("No hay planes de grupo activos", provider.Find("#no-group-plans").TextContent));
    }

    [Fact]
    public async Task The_group_dialog_lists_the_people_the_seats_and_who_pays()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();

        var groupId = Guid.NewGuid();
        MembershipResponse Membership(string status, bool holder) => new()
        {
            Id = Guid.NewGuid(), MemberId = Guid.NewGuid(), PlanName = "Familiar", Status = status, StartDate = Start, EndDate = End,
            GroupId = groupId, IsGroupHolder = holder,
        };
        var group = new MembershipGroupResponse
        {
            Id = groupId, PlanId = Guid.NewGuid(), PlanName = "Familiar", Seats = 4, SeatsUsed = 2, SeatsFree = 2,
            StartDate = Start, EndDate = End, TotalToCharge = 150000, HolderMemberId = Guid.NewGuid(), HolderMembershipId = Guid.NewGuid(),
            Members = new List<MembershipGroupMemberResponse>
            {
                new() { MemberId = Guid.NewGuid(), MemberName = "Ana Titular", IsHolder = true, Membership = Membership("Active", true) },
                new() { MemberId = Guid.NewGuid(), MemberName = "Beto Beneficiario", IsHolder = false, Membership = Membership("Active", false) },
                new() { MemberId = Guid.NewGuid(), MemberName = "Carla Salió", IsHolder = false, Membership = Membership("Cancelled", false) },
            },
        };
        var groups = System.Reflection.DispatchProxy.Create<IMembershipGroupsClient, FakeClient>();
        ((FakeClient)(object)groups).Handlers["GetMembershipGroupAsync"] = _ => group;
        Services.AddSingleton(groups);
        Services.AddSingleton(System.Reflection.DispatchProxy.Create<IMembersClient, FakeClient>());
        Services.AddSingleton(System.Reflection.DispatchProxy.Create<IPaymentsClient, FakeClient>());

        RenderComponent<MudPopoverProvider>();
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<MembershipGroupDialog> { { x => x.GroupId, groupId } };
        await provider.InvokeAsync(() => dialogs.ShowAsync<MembershipGroupDialog>("Plan de grupo", parameters));

        provider.WaitForAssertion(() =>
        {
            Assert.Contains("2 de 4 cupos", provider.Find("[data-testid=group-seats]").TextContent);
            Assert.Equal(3, provider.FindAll("[data-testid=group-member]").Count);
            Assert.Contains("Titular (paga)", provider.Markup);
        });

        // Solo el beneficiario activo se puede sacar: el titular y quien ya salió no.
        Assert.Single(provider.FindAll("button"), b => b.TextContent.Contains("Sacar del grupo"));
    }
}
