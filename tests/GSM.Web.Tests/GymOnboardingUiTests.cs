using System.Reflection;
using Bunit;
using Bunit.TestDoubles;
using GSM.Client.Generated;
using GSM.Panel.UI.Onboarding;
using GSM.Panel.UI.Pages.Onboarding;
using GSM.UI.Shared.Auth;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using static GSM.Web.Tests.CommercialStatusUiTests;
using OnboardingIndex = GSM.Panel.UI.Pages.Onboarding.Index;

namespace GSM.Web.Tests;

/// <summary>G5: asistente de onboarding, bloqueo del panel (OnboardingGate) y lista "Para completar".</summary>
public class GymOnboardingUiTests : TestContext
{
    public GymOnboardingUiTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    private static OnboardingResponse State(
        bool complete, string? next, bool locationDone = false, bool hoursDone = false,
        bool equipmentDone = false, bool plansDone = false, params string[] toComplete)
        => new()
        {
            IsComplete = complete,
            NextStep = next,
            Steps = new List<OnboardingStepResponse>
            {
                new() { Key = "gym", Order = 1, Required = true, Done = true },
                new() { Key = "location", Order = 2, Required = true, Done = locationDone },
                new() { Key = "hours", Order = 3, Required = true, Done = hoursDone },
                new() { Key = "equipment", Order = 4, Required = true, Done = equipmentDone },
                new() { Key = "plans", Order = 5, Required = true, Done = plansDone },
                new() { Key = "entry", Order = 6, Required = false, Done = false },
                new() { Key = "team", Order = 7, Required = false, Done = false },
            },
            ToComplete = toComplete.Select(k => new OnboardingTodoResponse { Key = k }).ToList(),
        };

    private FakeClient RegisterGym()
    {
        var proxy = DispatchProxy.Create<IGymClient, FakeClient>();
        Services.AddSingleton(proxy);
        return (FakeClient)(object)proxy;
    }

    private GymContext RegisterContext(string role)
    {
        var context = new GymContext();
        context.Set(new MeResponse { Role = role }, false);
        Services.AddSingleton(context);
        return context;
    }

    // ---- Plantillas de plan ----

    [Fact]
    public void Plan_templates_are_day_month_quarter_and_year_and_only_need_a_price()
    {
        Assert.Equal(["Día", "Mensual", "Trimestral", "Anual"], PlanTemplate.All.Select(t => t.Name).ToArray());

        var quarter = PlanTemplate.All.Single(t => t.Key == "quarter").ToRequest(250000m);
        Assert.Equal("Trimestral", quarter.Name);
        Assert.Equal(3, quarter.DurationValue);
        Assert.Equal("months", quarter.DurationUnit);
        Assert.Equal(250000d, quarter.Price);
        Assert.Equal("time", quarter.Kind);

        var day = PlanTemplate.All.Single(t => t.Key == "day").ToRequest(12000m);
        Assert.Equal(1, day.DurationValue);
        Assert.Equal("days", day.DurationUnit);
    }

    // ---- Bloqueo (OnboardingGate) ----

    [Fact]
    public async Task The_gate_blocks_an_owner_while_the_onboarding_is_pending()
    {
        RegisterGym().Handlers["OnboardingAsync"] = _ => State(false, "location");
        RegisterContext("owner");
        Services.AddScoped<OnboardingGate>();

        Assert.True(await Services.GetRequiredService<OnboardingGate>().IsBlockedAsync());
    }

    [Fact]
    public async Task The_gate_also_blocks_staff_who_then_see_the_waiting_message()
    {
        RegisterGym().Handlers["OnboardingAsync"] = _ => State(false, "location");
        RegisterContext("staff");
        Services.AddScoped<OnboardingGate>();

        Assert.True(await Services.GetRequiredService<OnboardingGate>().IsBlockedAsync());
    }

    [Theory]
    [InlineData("superadmin")]
    [InlineData("member")]
    [InlineData("kiosk")]
    public async Task The_gate_never_blocks_other_roles_and_does_not_even_ask(string role)
    {
        var fake = RegisterGym();
        fake.Handlers["OnboardingAsync"] = _ => State(false, "location");
        RegisterContext(role);
        Services.AddScoped<OnboardingGate>();

        Assert.False(await Services.GetRequiredService<OnboardingGate>().IsBlockedAsync());
        Assert.DoesNotContain(fake.Calls, c => c.Method == "OnboardingAsync");
    }

    [Fact]
    public async Task The_gate_fails_open_when_the_state_cannot_be_read()
    {
        RegisterGym().Handlers["OnboardingAsync"] = _ => throw new InvalidOperationException("sin red");
        RegisterContext("owner");
        Services.AddScoped<OnboardingGate>();

        Assert.False(await Services.GetRequiredService<OnboardingGate>().IsBlockedAsync());
    }

    [Fact]
    public async Task Once_complete_the_gate_stops_asking_during_the_session()
    {
        var fake = RegisterGym();
        fake.Handlers["OnboardingAsync"] = _ => State(true, null);
        RegisterContext("owner");
        Services.AddScoped<OnboardingGate>();
        var gate = Services.GetRequiredService<OnboardingGate>();

        Assert.False(await gate.IsBlockedAsync());
        Assert.False(await gate.IsBlockedAsync());
        Assert.False(await gate.IsBlockedAsync());

        Assert.Single(fake.Calls, c => c.Method == "OnboardingAsync");
    }

    // ---- Asistente ----

    [Fact]
    public void The_wizard_lists_the_seven_steps_and_starts_at_the_first_missing_required_one()
    {
        this.AddTestAuthorization().SetAuthorized("owner").SetRoles("owner");
        RegisterGym().Handlers["OnboardingAsync"] = _ => State(false, "equipment", locationDone: true, hoursDone: true);
        RegisterContext("owner");
        Services.AddScoped<OnboardingGate>();

        var cut = RenderComponent<OnboardingIndex>();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(7, cut.FindAll("[data-testid^='onboarding-step-']").Count);
            Assert.NotNull(cut.Find("[data-testid='onboarding-add-equipment']"));
        });
    }

    [Fact]
    public void Next_is_refused_while_the_required_step_has_no_data()
    {
        this.AddTestAuthorization().SetAuthorized("owner").SetRoles("owner");
        RegisterGym().Handlers["OnboardingAsync"] = _ => State(false, "equipment", locationDone: true, hoursDone: true);
        RegisterContext("owner");
        Services.AddScoped<OnboardingGate>();
        var cut = RenderComponent<OnboardingIndex>();
        cut.WaitForAssertion(() => cut.Find("[data-testid='onboarding-next']"));

        cut.Find("[data-testid='onboarding-next']").Click();

        cut.WaitForAssertion(() =>
            Assert.Contains("al menos una máquina", cut.Find("[data-testid='onboarding-error']").TextContent));
        Assert.NotNull(cut.Find("[data-testid='onboarding-add-equipment']"));
    }

    [Fact]
    public void A_gym_that_is_already_complete_is_sent_back_to_the_panel()
    {
        this.AddTestAuthorization().SetAuthorized("owner").SetRoles("owner");
        RegisterGym().Handlers["OnboardingAsync"] = _ =>
            State(true, null, true, true, true, true, "entry", "team");
        RegisterContext("owner");
        Services.AddScoped<OnboardingGate>();

        // Con el onboarding completo el asistente no tiene nada que hacer y devuelve al panel.
        var cut = RenderComponent<OnboardingIndex>();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid^='onboarding-step-']")));
    }

    // ---- Para completar ----

    [Fact]
    public void To_complete_lists_the_pending_optional_items_for_the_owner()
    {
        this.AddTestAuthorization().SetAuthorized("owner").SetRoles("owner");
        RegisterGym().Handlers["OnboardingAsync"] = _ =>
            State(true, null, true, true, true, true, "entry", "members");

        var cut = RenderComponent<ToCompleteCard>();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='to-complete-entry']"));
            Assert.NotNull(cut.Find("[data-testid='to-complete-members']"));
            Assert.Empty(cut.FindAll("[data-testid='to-complete-team']"));
        });
    }

    [Fact]
    public void To_complete_is_hidden_when_nothing_is_pending_or_the_user_is_not_the_owner()
    {
        this.AddTestAuthorization().SetAuthorized("ana").SetRoles("staff");
        RegisterGym().Handlers["OnboardingAsync"] = _ => State(true, null, true, true, true, true, "entry");

        var cut = RenderComponent<ToCompleteCard>();

        Assert.Empty(cut.FindAll("[data-testid='to-complete']"));
    }

    // ---- Planes sugeridos por tipo (T3) ----

    [Fact]
    public void Suggested_plans_follow_the_business_type_and_never_come_with_a_price()
    {
        Assert.Equal(["Día", "Mensual", "Trimestral", "Anual"], PlanTemplate.For("Gym").Select(t => t.Name));
        Assert.Equal(["Día", "Mensual", "Trimestral", "Anual"], PlanTemplate.For(null).Select(t => t.Name));
        Assert.Equal(["Mensual", "Tiquetera 12 clases"], PlanTemplate.For("Box").Select(t => t.Name));
        Assert.Equal(
            ["Tiquetera 8 clases", "Tiquetera 4 clases", "Clase suelta"], PlanTemplate.For("Studio").Select(t => t.Name));
        Assert.Equal(["Mensual", "Paquete 12 sesiones"], PlanTemplate.For("PersonalTrainer").Select(t => t.Name));
        Assert.Equal(["Mensual", "Trimestral"], PlanTemplate.For("Academy").Select(t => t.Name));

        var pass = PlanTemplate.For("Studio").Single(t => t.Key == "pass8").ToRequest(80000m);
        Assert.Equal("pass", pass.Kind);
        Assert.Equal(8, pass.Entries);
        Assert.Equal(30, pass.ValidityDays);
        Assert.Equal(30, pass.DurationValue);
        Assert.Equal("days", pass.DurationUnit);

        var single = PlanTemplate.For("Studio").Single(t => t.Key == "single").ToRequest(25000m);
        Assert.Equal(1, single.Entries);
        Assert.Equal(7, single.ValidityDays);

        // Un plan por tiempo no lleva entradas ni vigencia propias.
        var monthly = PlanTemplate.For("PersonalTrainer").Single(t => t.Key == "month30").ToRequest(150000m);
        Assert.Equal("time", monthly.Kind);
        Assert.Null(monthly.Entries);
        Assert.Null(monthly.ValidityDays);
        Assert.Equal(30, monthly.DurationValue);
    }

    [Fact]
    public void The_plans_step_offers_the_plans_of_the_gym_type_and_creates_a_pass_with_its_entries()
    {
        this.AddTestAuthorization().SetAuthorized("owner").SetRoles("owner");
        var gym = RegisterGym();
        gym.Handlers["OnboardingAsync"] = _ => State(false, "plans", true, true, true);
        gym.Handlers["GymGETAsync"] = _ => new GymResponse { Type = "Studio", Name = "Estudio Luna" };
        var (plans, plansFake) = Fake<IMembershipPlansClient>();
        plansFake.Handlers["MembershipPlansAllAsync"] = _ => new List<PlanResponse>();
        plansFake.Handlers["MembershipPlansPOSTAsync"] = _ => new PlanResponse { Id = Guid.NewGuid(), Name = "Tiquetera 8 clases" };
        Services.AddSingleton(plans);
        RegisterContext("owner");
        Services.AddScoped<OnboardingGate>();

        var cut = RenderComponent<OnboardingIndex>();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='onboarding-plan-pass8']")));
        Assert.Empty(cut.FindAll("[data-testid='onboarding-plan-year']"));
        Assert.Equal(3, cut.FindAll("[data-testid^='onboarding-plan-'][data-testid$='-price']").Count);

        cut.Find("[data-testid='onboarding-plan-pass8']").Change(true);
        cut.Find("[data-testid='onboarding-plan-pass8-price']").Change("80000");
        cut.Find("[data-testid='onboarding-plans-create']").Click();

        cut.WaitForAssertion(() =>
        {
            var call = Assert.Single(plansFake.Calls, c => c.Method == "MembershipPlansPOSTAsync");
            var request = Assert.IsType<CreatePlanRequest>(call.Args[0]);
            Assert.Equal("pass", request.Kind);
            Assert.Equal(8, request.Entries);
            Assert.Equal(80000d, request.Price);
        });
    }

    [Fact]
    public void A_personal_trainer_does_not_read_about_the_gym_entrance_in_the_wizard()
    {
        this.AddTestAuthorization().SetAuthorized("owner").SetRoles("owner");
        var gym = RegisterGym();
        gym.Handlers["OnboardingAsync"] = _ => State(false, "equipment", true, true);
        gym.Handlers["GymGETAsync"] = _ => new GymResponse { Type = "PersonalTrainer", Name = "Coach Ana" };
        RegisterContext("owner");
        Services.AddScoped<OnboardingGate>();

        var cut = RenderComponent<OnboardingIndex>();

        // El asistente abre en el primer paso obligatorio pendiente (ninguno): vamos al paso de entrada.
        cut.WaitForAssertion(() => cut.Find("[data-testid='onboarding-step-entry']"));
        cut.Find("[data-testid='onboarding-step-entry']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Entrenamientos", cut.Markup);
            Assert.DoesNotContain("kiosco", cut.Markup);
        });
    }
}
