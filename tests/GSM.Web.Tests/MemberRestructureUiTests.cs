using System.Reflection;
using Bunit;
using GSM.Client.Generated;
using GSM.Member.UI;
using GSM.UI.Shared.Auth;
using GSM.UI.Shared.Formatting;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using static GSM.Web.Tests.CommercialStatusUiTests;

namespace GSM.Web.Tests;

/// <summary>
/// La app del miembro reordenada: el planificador de la semana en Entrenar y la pestaña Fuerza (récords) dentro de
/// Progreso.
/// </summary>
public class MemberRestructureUiTests : TestContext
{
    private FakeClient RegisterMe()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var me = DispatchProxy.Create<IMeClient, FakeClient>();
        Services.AddSingleton(me);
        return (FakeClient)(object)me;
    }

    private static MyRoutineWeekResponse Week(Guid chest, Guid legs)
    {
        var today = GsmFormat.Today();
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var days = Enumerable.Range(0, 7).Select(i =>
        {
            var date = monday.AddDays(i);
            var training = i is 0 or 2;
            return new RoutineWeekDayView
            {
                Date = date,
                DayOfWeek = i + 1,
                IsRest = !training,
                DayId = i == 0 ? chest : i == 2 ? legs : null,
                DayName = i == 0 ? "Pecho" : i == 2 ? "Pierna" : null,
                Done = i == 0,
                IsToday = date == today,
            };
        }).ToList();
        return new MyRoutineWeekResponse { WeekStart = monday, HasRoutine = true, Days = days };
    }

    [Fact]
    public void The_planner_shows_the_week_with_the_done_count_and_a_day_can_be_picked()
    {
        var me = RegisterMe();
        var chest = Guid.NewGuid();
        var legs = Guid.NewGuid();
        me.Handlers["GetMyRoutineWeekAsync"] = _ => Week(chest, legs);
        Guid? picked = Guid.Empty;

        var cut = RenderComponent<RoutineWeekPlanner>(p => p
            .Add(x => x.TodayDayId, null)
            .Add(x => x.OnPick, (Guid? id) => picked = id));

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='week-planner']")));
        Assert.Contains("Esta semana", cut.Find("[data-testid='planner-label']").TextContent);
        Assert.Contains("1 de 2", cut.Find("[data-testid='planner-done']").TextContent);
        Assert.Equal(7, cut.FindAll(".gsm-m-planner__day").Count);
        Assert.Equal(5, cut.FindAll(".gsm-m-planner__day.is-rest").Count);

        var legsDay = cut.FindAll(".gsm-m-planner__day").Single(b => b.GetAttribute("aria-label")!.Contains("Pierna"));
        legsDay.Click();
        Assert.Equal(legs, picked);
    }

    [Fact]
    public void Moving_to_another_week_asks_for_that_date_and_lists_its_days()
    {
        var me = RegisterMe();
        var chest = Guid.NewGuid();
        var legs = Guid.NewGuid();
        me.Handlers["GetMyRoutineWeekAsync"] = _ => Week(chest, legs);

        var cut = RenderComponent<RoutineWeekPlanner>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='planner-next']")));

        cut.Find("[data-testid='planner-next'] button, button[data-testid='planner-next']").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='planner-list']")));
        var asked = me.Calls.Where(c => c.Method == "GetMyRoutineWeekAsync").Select(c => c.Args[0]).ToList();
        Assert.Null(asked[0]);
        Assert.Equal(GsmFormat.Today().AddDays(7), asked[^1]);
        Assert.NotEmpty(cut.FindAll("[data-testid='planner-today']"));
    }

    [Fact]
    public void Without_a_routine_the_planner_is_not_shown()
    {
        var me = RegisterMe();
        me.Handlers["GetMyRoutineWeekAsync"] = _ => new MyRoutineWeekResponse { HasRoutine = false, Days = new List<RoutineWeekDayView>() };

        var cut = RenderComponent<RoutineWeekPlanner>();

        Assert.Empty(cut.FindAll("[data-testid='week-planner']"));
    }

    [Fact]
    public void Progress_has_constancia_and_fuerza_and_fuerza_shows_the_records()
    {
        var me = RegisterMe();
        me.Handlers["GetMyStreakCalendarAsync"] = _ => new StreakCalendarResponse { Year = 2026, Month = 11, Days = new List<StreakCalendarDay>() };
        me.Handlers["GetMyProgressAsync"] = _ => new List<ExerciseProgressSummary>();
        var gym = new GymContext();
        gym.Set(new MeResponse { FeatureFlags = new MeFeatureFlags { ShowRoutines = true, ShowGamification = true } }, false);
        Services.AddSingleton(gym);
        RenderComponent<MudPopoverProvider>();

        var cut = RenderComponent<GSM.Member.UI.Pages.Achievements>();

        cut.WaitForAssertion(() => Assert.Contains("Constancia", cut.Find("[data-testid='tab-racha']").TextContent));
        cut.Find("[data-testid='tab-fuerza']").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='panel-fuerza']")));
        Assert.Contains(me.Calls, c => c.Method == "GetMyProgressAsync");
    }

    [Fact]
    public void Without_routines_progress_has_no_fuerza_tab()
    {
        var me = RegisterMe();
        me.Handlers["GetMyStreakCalendarAsync"] = _ => new StreakCalendarResponse { Year = 2026, Month = 11, Days = new List<StreakCalendarDay>() };
        var gym = new GymContext();
        gym.Set(new MeResponse { FeatureFlags = new MeFeatureFlags { ShowRoutines = false } }, false);
        Services.AddSingleton(gym);
        RenderComponent<MudPopoverProvider>();

        var cut = RenderComponent<GSM.Member.UI.Pages.Achievements>();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='tab-racha']")));
        Assert.Empty(cut.FindAll("[data-testid='tab-fuerza']"));
    }
}
