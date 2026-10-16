using System.Reflection;
using Bunit;
using GSM.Client.Generated;
using GSM.UI.Shared.Auth;
using GSM.UI.Shared.Formatting;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using static GSM.Web.Tests.CommercialStatusUiTests;

namespace GSM.Web.Tests;

/// <summary>
/// MS8 ("Mi membresía") y L7 ("Mi progreso": pestañas, calendario de racha). Escritos contra el cliente tal como queda
/// tras regenerar: <c>GetMyStreakCalendarAsync</c> y <c>GetMyMembershipsAsync</c> en <c>IMeClient</c>.
/// </summary>
public class MyProgressUiTests : TestContext
{
    public MyProgressUiTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    private FakeClient RegisterMe()
    {
        var proxy = DispatchProxy.Create<IMeClient, FakeClient>();
        Services.AddSingleton(proxy);
        return (FakeClient)(object)proxy;
    }

    private void RegisterGym(bool showRanking = true)
    {
        var gym = new GymContext();
        gym.Set(
            new MeResponse { FeatureFlags = new MeFeatureFlags { ShowGamification = true, ShowRanking = showRanking } },
            impersonating: false);
        Services.AddSingleton(gym);
    }

    private static StreakCalendarResponse Calendar(string todayKind = "train", string? note = null)
    {
        var today = GsmFormat.Today();
        var first = new DateOnly(today.Year, today.Month, 1);
        var days = Enumerable.Range(0, DateTime.DaysInMonth(first.Year, first.Month))
            .Select(i => first.AddDays(i))
            .Select(d => new StreakCalendarDay
            {
                Date = d,
                Kind = d == today ? "pending" : d == today.AddDays(-1) ? "trained" : d == today.AddDays(-2) ? "frozen" : d == today.AddDays(-3) ? "missed" : "none",
                Note = d == today.AddDays(-4) ? note : null,
            })
            .ToList();
        return new StreakCalendarResponse
        {
            Year = first.Year,
            Month = first.Month,
            CurrentStreak = 5,
            BestStreak = 12,
            TodayKind = todayKind,
            HasRoutine = true,
            FreezesAvailable = 1,
            NextFreezeOn = today.AddDays(9),
            FreezesUsedOn = new List<DateOnly> { today.AddDays(-2) },
            HistoryStart = first.AddMonths(-2),
            Days = days,
        };
    }

    // ---------- pestañas ----------

    [Fact]
    public void My_progress_opens_on_the_streak_tab_and_switches_to_achievements()
    {
        RegisterGym();
        var me = RegisterMe();
        me.Handlers["GetMyStreakCalendarAsync"] = _ => Calendar();
        me.Handlers["AchievementsAll2Async"] = _ => new List<MemberAchievementItem>();

        RenderComponent<MudPopoverProvider>();
        var cut = RenderComponent<GSM.Member.UI.Pages.Achievements>();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='streak-calendar']")));
        cut.Find("[data-testid='tab-logros']").Click();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid='streak-calendar']")));
        Assert.NotEmpty(cut.FindAll("[data-testid='panel-logros']"));
    }

    [Fact]
    public void The_tab_query_parameter_opens_that_tab()
    {
        RegisterGym();
        var me = RegisterMe();
        me.Handlers["AchievementsAll2Async"] = _ => new List<MemberAchievementItem>();

        RenderComponent<MudPopoverProvider>();
        var nav = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        nav.NavigateTo(Microsoft.AspNetCore.Components.NavigationManagerExtensions.GetUriWithQueryParameter(nav, "tab", "logros"));
        var cut = RenderComponent<GSM.Member.UI.Pages.Achievements>();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='panel-logros']")));
        Assert.DoesNotContain(me.Calls, c => c.Method == "GetMyStreakCalendarAsync");
    }

    // ---------- Racha ----------

    [Fact]
    public void The_streak_tab_shows_current_best_freezes_and_what_today_asks_for()
    {
        RegisterGym();
        var me = RegisterMe();
        me.Handlers["GetMyStreakCalendarAsync"] = _ => Calendar("train");

        RenderComponent<MudPopoverProvider>();
        var cut = RenderComponent<GSM.Member.UI.MyProgress.StreakPanel>();

        cut.WaitForAssertion(() => Assert.Contains("5", cut.Find("[data-testid='streak-current']").TextContent));
        Assert.Contains("12", cut.Find("[data-testid='streak-best']").TextContent);
        Assert.Contains("1", cut.Find("[data-testid='streak-freezes']").TextContent);
        Assert.Contains("Hoy te toca entrenar", cut.Find("[data-testid='streak-today']").TextContent);
        Assert.NotEmpty(cut.FindAll("[data-testid='streak-next-freeze']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='streak-freezes-used']"));
    }

    [Theory]
    [InlineData("rest", "día de descanso")]
    [InlineData("trained", "ya entrenaste")]
    public void The_streak_tab_tells_a_rest_day_and_a_trained_day_apart(string kind, string expected)
    {
        RegisterGym();
        var me = RegisterMe();
        me.Handlers["GetMyStreakCalendarAsync"] = _ => Calendar(kind);

        RenderComponent<MudPopoverProvider>();
        var cut = RenderComponent<GSM.Member.UI.MyProgress.StreakPanel>();

        cut.WaitForAssertion(() => Assert.Contains(expected, cut.Find("[data-testid='streak-today']").TextContent));
    }

    [Fact]
    public void Each_calendar_day_carries_its_kind_and_the_uncounted_note_is_explained()
    {
        RegisterGym();
        var today = GsmFormat.Today();
        var me = RegisterMe();
        me.Handlers["GetMyStreakCalendarAsync"] = _ => Calendar(note: "Tu entrada de este día no contó: no compartiste tu ubicación.");

        RenderComponent<MudPopoverProvider>();
        var cut = RenderComponent<GSM.Member.UI.MyProgress.StreakPanel>();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='streak-calendar']")));
        Assert.Equal("trained", cut.Find($"[data-testid='streak-day-{today.AddDays(-1):yyyy-MM-dd}']").GetAttribute("data-kind"));
        Assert.Equal("frozen", cut.Find($"[data-testid='streak-day-{today.AddDays(-2):yyyy-MM-dd}']").GetAttribute("data-kind"));
        Assert.Equal("missed", cut.Find($"[data-testid='streak-day-{today.AddDays(-3):yyyy-MM-dd}']").GetAttribute("data-kind"));
        Assert.Contains("ubicación", cut.Find($"[data-testid='streak-note-{today.AddDays(-4):yyyy-MM-dd}']").TextContent);
    }

    [Fact]
    public void Going_to_the_previous_month_asks_for_it_and_the_next_button_is_off_on_the_current_month()
    {
        RegisterGym();
        var me = RegisterMe();
        me.Handlers["GetMyStreakCalendarAsync"] = _ => Calendar();

        RenderComponent<MudPopoverProvider>();
        var cut = RenderComponent<GSM.Member.UI.MyProgress.StreakPanel>();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='streak-calendar']")));
        Assert.True(cut.Find("[data-testid='streak-next']").HasAttribute("disabled"));

        cut.Find("[data-testid='streak-prev']").Click();

        cut.WaitForAssertion(() =>
        {
            var previous = GsmFormat.Today().AddMonths(-1);
            var call = me.Calls.Last(c => c.Method == "GetMyStreakCalendarAsync");
            Assert.Equal(previous.Year, call.Args[0]);
            Assert.Equal(previous.Month, call.Args[1]);
        });
    }

    // ---------- Logros ----------

    [Fact]
    public void Achievements_split_into_earned_with_date_and_locked_closest_first()
    {
        RegisterGym();
        var far = Guid.NewGuid();
        var close = Guid.NewGuid();
        var earned = Guid.NewGuid();
        var me = RegisterMe();
        me.Handlers["AchievementsAll2Async"] = _ => new List<MemberAchievementItem>
        {
            new() { AchievementId = far, Name = "Lejano", ConditionType = "Visits", ConditionValue = 100, Progress = 5, RewardStatus = "None" },
            new() { AchievementId = close, Name = "Cercano", ConditionType = "Visits", ConditionValue = 10, Progress = 9, RewardStatus = "None" },
            new()
            {
                AchievementId = earned, Name = "Ganado", ConditionType = "Visits", ConditionValue = 3, Progress = 3, Unlocked = true,
                UnlockedAt = new DateTimeOffset(2026, 10, 5, 15, 0, 0, TimeSpan.Zero), RewardStatus = "None",
            },
        };

        RenderComponent<MudPopoverProvider>();
        var cut = RenderComponent<GSM.Member.UI.MyProgress.AchievementsPanel>();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='achievements-earned']")));
        Assert.Contains("Ganado el", cut.Find($"[data-testid='achievement-{earned}'] [data-testid='earned-date']").TextContent);
        var locked = cut.Find("[data-testid='achievements-locked']").InnerHtml;
        Assert.True(locked.IndexOf("Cercano", StringComparison.Ordinal) < locked.IndexOf("Lejano", StringComparison.Ordinal));
    }

    // ---------- Mi membresía (MS8) ----------

    private static MyMembershipsResponse Memberships() => new()
    {
        Access = new AccessStatusResponse
        {
            Commercial = "current", Kind = "covered", CanEnter = true, EntriesLeft = 4, EntriesTotal = 12,
            EndsOn = new DateOnly(2030, 11, 30), Notice = "Te quedan 4 de 12 entradas.",
        },
        Items = new List<MyMembershipItem>
        {
            new()
            {
                Id = Guid.NewGuid(), PlanName = "Bono 12", Kind = "pass", Status = "Active",
                StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2030, 11, 30),
                EntriesTotal = 12, EntriesUsed = 8, EntriesLeft = 4,
                Freezes = new List<MyFreezeItem>(),
                Entries = new List<MyPassEntryItem> { new() { UsedOn = new DateOnly(2026, 10, 2) }, new() { UsedOn = new DateOnly(2026, 10, 4) } },
            },
            new()
            {
                Id = Guid.NewGuid(), PlanName = "Mensual", Kind = "time", Status = "Expired",
                StartDate = new DateOnly(2026, 8, 1), EndDate = new DateOnly(2026, 8, 30),
                Freezes = new List<MyFreezeItem>
                {
                    new() { StartsOn = new DateOnly(2026, 8, 10), EndsOn = new DateOnly(2026, 8, 14), Reason = "Viaje", State = "ended", DaysCredited = 5 },
                },
                Entries = new List<MyPassEntryItem>(),
            },
        },
    };

    [Fact]
    public void My_membership_shows_the_pass_balance_the_history_and_the_freezes()
    {
        var me = RegisterMe();
        me.Handlers["GetMyMembershipsAsync"] = _ => Memberships();

        var cut = RenderComponent<GSM.Member.UI.Pages.MyMembership>();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='membership-access']")));
        Assert.Contains("Al día", cut.Find("[data-testid='membership-commercial']").TextContent);
        Assert.Contains("4 entradas", cut.Find("[data-testid='membership-entries-line']").TextContent);
        Assert.Contains("de 12", cut.Find("[data-testid='membership-entries-line']").TextContent);
        Assert.Contains("Bono 12", cut.Markup);
        Assert.Contains("8 de 12 entradas usadas", cut.Markup);
        Assert.NotEmpty(cut.FindAll("[data-testid='entry-days']"));
        var freezes = cut.Find("[data-testid='freezes']").TextContent;
        Assert.Contains("Viaje", freezes);
        Assert.Contains("5 días", freezes);
    }

    [Fact]
    public void My_membership_without_memberships_shows_an_empty_state()
    {
        var me = RegisterMe();
        me.Handlers["GetMyMembershipsAsync"] = _ => new MyMembershipsResponse
        {
            Access = new AccessStatusResponse { Commercial = "no_plan", Kind = "blocked", CanEnter = false },
            Items = new List<MyMembershipItem>(),
        };

        var cut = RenderComponent<GSM.Member.UI.Pages.MyMembership>();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='membership-empty']")));
        Assert.Contains("Sin plan", cut.Find("[data-testid='membership-commercial']").TextContent);
    }
}
