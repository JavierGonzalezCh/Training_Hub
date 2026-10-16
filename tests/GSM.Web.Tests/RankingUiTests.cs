using System.Reflection;
using Bunit;
using GSM.Client.Generated;
using GSM.UI.Shared.Auth;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using static GSM.Web.Tests.CommercialStatusUiTests;

namespace GSM.Web.Tests;

/// <summary>
/// L6: la pantalla del ranking voluntario en la app del miembro. Escrita contra el cliente tal como queda tras
/// regenerar (<c>GetMyRankingAsync</c>, <c>JoinMyRankingAsync</c>, <c>LeaveMyRankingAsync</c>).
/// </summary>
public class RankingUiTests : TestContext
{
    public RankingUiTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    private FakeClient Register<T>() where T : class
    {
        var proxy = DispatchProxy.Create<T, FakeClient>();
        Services.AddSingleton(proxy);
        return (FakeClient)(object)proxy;
    }

    private void RegisterGym(bool showRanking = true, bool showGamification = true)
    {
        var gym = new GymContext();
        gym.Set(
            new MeResponse { FeatureFlags = new MeFeatureFlags { ShowGamification = showGamification, ShowRanking = showRanking } },
            impersonating: false);
        Services.AddSingleton(gym);
    }

    private static RankingEntryResponse Entry(int position, string name, int days, bool isMe = false, string? photo = null)
        => new() { Position = position, DisplayName = name, Days = days, IsMe = isMe, PhotoUrl = photo };

    private static RankingResponse Ranking(RankingMeResponse me, params RankingEntryResponse[] top) => new()
    {
        Year = 2026,
        Month = 10,
        Top = top.ToList(),
        Me = me,
        PreviousYear = 2026,
        PreviousMonth = 9,
        PreviousPodium = new List<RankingEntryResponse> { Entry(1, "Camila R.", 22), Entry(2, "Fuerza21", 20), Entry(3, "Luis P.", 19) },
    };

    private static RankingMeResponse NotParticipating(bool canParticipate = true, string? reason = null) => new()
    {
        Participating = false,
        CanParticipate = canParticipate,
        CannotParticipateReason = reason,
    };

    private IRenderedComponent<GSM.Member.UI.Pages.Ranking> RenderRanking()
    {
        RenderComponent<MudPopoverProvider>();
        return RenderComponent<GSM.Member.UI.Pages.Ranking>();
    }

    [Fact]
    public void A_participant_sees_the_top_the_own_position_and_the_previous_podium()
    {
        RegisterGym();
        var me = Register<IMeClient>();
        me.Handlers["GetMyRankingAsync"] = _ => Ranking(
            new RankingMeResponse { Participating = true, CanParticipate = true, Position = 14, Days = 12 },
            Entry(1, "Ana T.", 20), Entry(2, "Fuerza21", 18, isMe: false), Entry(3, "Mateo G.", 15, isMe: true));

        var cut = RenderRanking();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Vas 14.º con 12 días", cut.Find("[data-testid='ranking-me']").TextContent);
            Assert.Contains("Ana T.", cut.Find("[data-testid='ranking-row-1']").TextContent);
            Assert.Contains("(tú)", cut.Find("[data-testid='ranking-row-3']").TextContent);
            Assert.Contains("Camila R.", cut.Find("[data-testid='podium-row-1']").TextContent);
        });
    }

    [Fact]
    public void A_participant_without_days_this_month_is_told_so_instead_of_a_position()
    {
        RegisterGym();
        var me = Register<IMeClient>();
        me.Handlers["GetMyRankingAsync"] = _ => Ranking(
            new RankingMeResponse { Participating = true, CanParticipate = true, Position = null, Days = 0 });

        var cut = RenderRanking();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Aún no sumas días", cut.Find("[data-testid='ranking-me']").TextContent);
            Assert.NotNull(cut.Find("[data-testid='ranking-empty']"));
        });
    }

    [Fact]
    public void Someone_who_does_not_participate_sees_the_top_and_an_invitation()
    {
        RegisterGym();
        var me = Register<IMeClient>();
        me.Handlers["GetMyRankingAsync"] = _ => Ranking(NotParticipating(), Entry(1, "Ana T.", 20));

        var cut = RenderRanking();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='ranking-invite']"));
            Assert.NotNull(cut.Find("[data-testid='ranking-row-1']"));
            Assert.NotNull(cut.Find("[data-testid='ranking-switch']"));
        });
        Assert.Empty(cut.FindAll("[data-testid='ranking-me']"));
    }

    [Fact]
    public async Task Turning_the_switch_on_joins_with_the_real_name_by_default()
    {
        RegisterGym();
        var me = Register<IMeClient>();
        me.Handlers["GetMyRankingAsync"] = _ => Ranking(NotParticipating(), Entry(1, "Ana T.", 20));
        me.Handlers["JoinMyRankingAsync"] = _ => new RankingMeResponse { Participating = true, CanParticipate = true };

        var cut = RenderRanking();
        cut.WaitForAssertion(() => cut.Find("[data-testid='ranking-switch']"));

        var toggle = cut.FindComponent<MudSwitch<bool>>();
        await cut.InvokeAsync(() => toggle.Instance.ValueChanged.InvokeAsync(true));

        var call = Assert.Single(me.Calls, c => c.Method == "JoinMyRankingAsync");
        var body = Assert.IsType<JoinRankingRequest>(call.Args[0]);
        Assert.False(body.UseAlias);
        Assert.Null(body.Alias);
    }

    [Fact]
    public async Task Turning_the_switch_off_leaves_the_ranking()
    {
        RegisterGym();
        var me = Register<IMeClient>();
        me.Handlers["GetMyRankingAsync"] = _ => Ranking(
            new RankingMeResponse { Participating = true, CanParticipate = true, Position = 1, Days = 5 },
            Entry(1, "Ana T.", 5, isMe: true));

        var cut = RenderRanking();
        cut.WaitForAssertion(() => cut.Find("[data-testid='ranking-switch']"));

        var toggle = cut.FindComponent<MudSwitch<bool>>();
        await cut.InvokeAsync(() => toggle.Instance.ValueChanged.InvokeAsync(false));

        Assert.Single(me.Calls, c => c.Method == "LeaveMyRankingAsync");
    }

    [Fact]
    public void A_minor_sees_the_ranking_but_cannot_join_and_is_told_why()
    {
        RegisterGym();
        var me = Register<IMeClient>();
        me.Handlers["GetMyRankingAsync"] = _ => Ranking(
            NotParticipating(canParticipate: false, reason: "Los menores de edad no pueden aparecer en el ranking."),
            Entry(1, "Ana T.", 20));

        var cut = RenderRanking();

        cut.WaitForAssertion(() =>
            Assert.Contains("menores de edad", cut.Find("[data-testid='ranking-cannot']").TextContent));
        Assert.Empty(cut.FindAll("[data-testid='ranking-switch']"));
    }

    [Fact]
    public void When_the_gym_turned_the_ranking_off_nothing_is_requested()
    {
        RegisterGym(showRanking: false);
        var me = Register<IMeClient>();

        var cut = RenderRanking();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='ranking-off']")));
        Assert.DoesNotContain(me.Calls, c => c.Method == "GetMyRankingAsync");
    }

    [Fact]
    public void My_progress_offers_the_ranking_tab_only_when_the_gym_has_it_on()
    {
        RegisterGym(showRanking: true);
        var me = Register<IMeClient>();
        me.Handlers["AchievementsAll2Async"] = _ => new List<MemberAchievementItem>();
        me.Handlers["GetMyStreakCalendarAsync"] = _ => new StreakCalendarResponse { Year = 2026, Month = 10, Days = new List<StreakCalendarDay>() };

        RenderComponent<MudPopoverProvider>();
        var on = RenderComponent<GSM.Member.UI.Pages.Achievements>();

        on.WaitForAssertion(() => Assert.NotEmpty(on.FindAll("[data-testid='tab-ranking']")));
    }

    [Fact]
    public void My_progress_hides_the_ranking_tab_when_the_gym_turned_it_off()
    {
        RegisterGym(showRanking: false);
        var me = Register<IMeClient>();
        me.Handlers["AchievementsAll2Async"] = _ => new List<MemberAchievementItem>();
        me.Handlers["GetMyStreakCalendarAsync"] = _ => new StreakCalendarResponse { Year = 2026, Month = 10, Days = new List<StreakCalendarDay>() };

        RenderComponent<MudPopoverProvider>();
        var cut = RenderComponent<GSM.Member.UI.Pages.Achievements>();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='tab-logros']")));
        Assert.Empty(cut.FindAll("[data-testid='tab-ranking']"));
    }
}
