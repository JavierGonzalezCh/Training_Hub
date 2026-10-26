using Bunit;
using GSM.Client.Generated;
using GSM.Member.UI;
using GSM.UI.Shared.Components;
using GSM.UI.Shared.Routines;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using static GSM.Web.Tests.CommercialStatusUiTests;

namespace GSM.Web.Tests;

/// <summary>T6 — el cumplimiento de la rutina en la app del miembro, en la ficha y en el listado del panel.</summary>
public class RoutineAdherenceUiTests : TestContext
{
    private static readonly DateOnly Monday = new(2026, 3, 2);

    private static AdherenceWeekItem Week(int planned, int done, int due, int? percent, bool withDays = false)
        => new()
        {
            WeekStart = Monday,
            PlannedDays = planned,
            DoneDays = done,
            DueDays = due,
            OffPlanDays = 0,
            CompliancePercent = percent,
            Days = withDays
                ?
                [
                    new AdherenceDayItem { Date = Monday, Status = "done", PlannedExercises = 4, DoneExercises = 4 },
                    new AdherenceDayItem { Date = Monday.AddDays(1), Status = "rest" },
                    new AdherenceDayItem { Date = Monday.AddDays(2), Status = "missed", PlannedExercises = 4, DoneExercises = 0 },
                ]
                : null,
        };

    private void Setup()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddScoped<ViewportState>();
    }

    private void Pin() => Services.GetRequiredService<ViewportState>().Pin(true);

    [Fact]
    public void The_member_sees_their_week_as_days_done_of_days_planned_without_a_percentage()
    {
        Setup();
        var (me, fake) = Fake<IMeClient>();
        fake.Handlers["GetMyAdherenceAsync"] = _ => new MemberAdherence
        {
            HasRoutine = true,
            Weeks = new List<AdherenceWeekItem> { Week(4, 2, 3, 67, withDays: true) },
        };
        Services.AddSingleton(me);
        Pin();

        var cut = RenderComponent<WeekAdherenceCard>();

        var card = cut.Find("[data-testid='week-adherence']");
        Assert.Contains("Esta semana: 2 de 4 días", card.TextContent);
        Assert.DoesNotContain("%", card.TextContent);
    }

    [Fact]
    public void The_member_card_is_hidden_when_the_week_has_no_planned_days_or_the_read_returns_nothing()
    {
        Setup();
        var (me, fake) = Fake<IMeClient>();
        fake.Handlers["GetMyAdherenceAsync"] = _ => new MemberAdherence
        {
            HasRoutine = true,
            Weeks = new List<AdherenceWeekItem> { Week(0, 0, 0, null) },
        };
        Services.AddSingleton(me);
        Pin();

        var cut = RenderComponent<WeekAdherenceCard>();

        Assert.Empty(cut.FindAll("[data-testid='week-adherence']"));
    }

    [Fact]
    public void The_member_profile_panel_shows_one_bar_per_week_and_the_current_week_by_day()
    {
        Setup();
        var data = new MemberAdherence
        {
            HasRoutine = true,
            Weeks = new List<AdherenceWeekItem> { Week(3, 1, 3, 33), Week(3, 3, 3, 100, withDays: true) },
        };

        var cut = RenderComponent<AdherencePanel>(p => p.Add(c => c.Load, () => Task.FromResult(data)));

        Assert.Equal(2, cut.FindAll("[data-testid='adherence-week']").Count);
        Assert.Contains("Esta semana: 3 de 3 días", cut.Find("[data-testid='adherence-current']").TextContent);
        Assert.Single(cut.FindAll("[data-testid='adherence-day-done']"));
        Assert.Single(cut.FindAll("[data-testid='adherence-day-missed']"));
    }

    [Fact]
    public void The_member_profile_panel_says_so_when_the_member_has_no_routine()
    {
        Setup();
        var data = new MemberAdherence { HasRoutine = false, Weeks = new List<AdherenceWeekItem>() };

        var cut = RenderComponent<AdherencePanel>(p => p.Add(c => c.Load, () => Task.FromResult(data)));

        Assert.NotEmpty(cut.FindAll("[data-testid='adherence-empty']"));
    }

    [Fact]
    public void The_list_shows_each_member_and_the_behind_switch_asks_the_server_for_only_those()
    {
        Setup();
        var (routines, fake) = Fake<IRoutinesClient>();
        fake.Handlers["ListRoutineAdherenceAsync"] = args => new PagedResultOfMemberAdherenceRow
        {
            Items = new List<MemberAdherenceRow>
            {
                new()
                {
                    MemberId = Guid.NewGuid(), FullName = "Ana Prueba", RoutineName = "Fuerza", WeekStart = Monday,
                    PlannedDays = 4, DoneDays = 1, CurrentWeekPercent = 25, RecentPercent = 40, LastLogOn = Monday,
                },
            },
            Page = 1, PageSize = 25, Total = 1,
        };
        Services.AddSingleton(routines);
        Pin();

        RenderComponent<MudBlazor.MudPopoverProvider>();
        var cut = RenderComponent<GSM.Panel.UI.Pages.RoutineAdherence.Index>();

        cut.WaitForAssertion(() => Assert.Contains("Ana Prueba", cut.Markup));
        Assert.Contains("1 de 4 días", cut.Markup);
        Assert.Contains("40 %", cut.Markup);

        cut.Find("[data-testid='adherence-behind']").Change(true);

        cut.WaitForAssertion(() => Assert.Contains(
            fake.Calls, c => c.Method == "ListRoutineAdherenceAsync" && c.Args[1] is true));
    }
}
