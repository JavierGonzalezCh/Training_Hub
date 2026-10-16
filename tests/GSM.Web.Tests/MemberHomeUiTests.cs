using System.Reflection;
using Bunit;
using GSM.Client.Generated;
using GSM.Member.UI;
using GSM.Member.UI.Auth;
using GSM.Member.UI.Pages;
using GSM.UI.Shared.Auth;
using GSM.UI.Shared.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using static GSM.Web.Tests.CommercialStatusUiTests;

namespace GSM.Web.Tests;

/// <summary>
/// D2 — el inicio del miembro: una sola llamada (<c>GetMyHomeAsync</c>), los ocho bloques en el orden de
/// <c>GSM-dashboards.md</c> §2 (una columna en el celular; 1-3-5 a la izquierda y 2-4-6-7-8 a la derecha en
/// escritorio) y un bloque nulo simplemente no se pinta. Escritos contra el cliente tal como queda tras regenerarlo.
/// </summary>
public class MemberHomeUiTests : TestContext
{
    private sealed class NoScanner : IQrScanner
    {
        public bool IsSupported => false;

        public Task<string?> ScanAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private FakeClient _me = null!;

    private static MemberHomeResponse FullHome() => new()
    {
        Status = new HomeStatusBlock
        {
            Access = new AccessStatusResponse
            {
                Commercial = "expiring", Kind = "expiring", CanEnter = true, DaysLeft = 4,
                Notice = "Tu plan vence en 4 días.",
            },
            PlanName = "Mensual",
        },
        Gym = new HomeGymBlock
        {
            OpenStatus = new GymOpenStatusDto { HasSchedule = true, IsOpen = true, Text = "Abierto · cierra a las 22:00" },
            OccupancyLevel = "moderate",
        },
        Entry = new HomeEntryBlock { CheckedInToday = true, CheckedInAt = new DateTimeOffset(2026, 6, 15, 11, 12, 0, TimeSpan.Zero) },
        Pending = new HomePendingBlock
        {
            WaitlistOffers = new List<HomeWaitlistOffer>
            {
                new()
                {
                    ClassId = Guid.NewGuid(), ClassName = "Spinning", SessionDate = new DateOnly(2026, 6, 16),
                    StartTime = new TimeOnly(7, 0), ExpiresAt = new DateTimeOffset(2026, 6, 15, 15, 0, 0, TimeSpan.Zero),
                },
            },
            Rewards = new List<HomeReward>
            {
                new() { AchievementId = Guid.NewGuid(), AchievementName = "100 visitas", RewardDescription = "Una camiseta" },
            },
        },
        Training = new HomeTrainingBlock
        {
            RoutineName = "Fuerza", DayName = "Pecho y tríceps", IsRest = false, ExerciseCount = 6,
            MuscleGroups = new List<string> { "Chest" },
            ReviewOn = new DateOnly(2026, 6, 18), ReviewDaysLeft = 3,
        },
        NextClass = new HomeNextClassBlock
        {
            ClassId = Guid.NewGuid(), ClassName = "Yoga", SessionDate = new DateOnly(2026, 6, 16),
            StartTime = new TimeOnly(18, 30), InstructorName = "Laura",
        },
        Progress = new HomeProgressBlock
        {
            CurrentStreak = 9, FreezesAvailable = 2, TotalVisits = 40,
            Ranking = new HomeRankingStanding { Participating = true, Position = 3, Days = 12 },
        },
        News = new HomeNewsBlock
        {
            Featured = new HomeFeaturedAnnouncement { Id = Guid.NewGuid(), Title = "Cierre por festivo", Pinned = true },
            UnreadAnnouncements = 2,
        },
    };

    private IRenderedComponent<Home> RenderHome(MemberHomeResponse? home, bool narrow)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddScoped<ViewportState>();

        var me = DispatchProxy.Create<IMeClient, FakeClient>();
        _me = (FakeClient)(object)me;
        _me.Handlers["GetMyHomeAsync"] = _ => home;
        var consents = DispatchProxy.Create<IMeConsentsClient, FakeClient>();
        ((FakeClient)(object)consents).Handlers["ListMyConsentsAsync"] = _ => new List<MemberConsentStatus>();
        Services.AddSingleton(me);
        Services.AddSingleton(consents);
        Services.AddSingleton<IQrScanner>(new NoScanner());

        var gym = new GymContext();
        gym.Set(new MeResponse
        {
            FeatureFlags = new MeFeatureFlags
            {
                ShowRoutines = true, ShowClasses = true, ShowCommunity = true, ShowGamification = true, CheckinQr = true,
            },
        }, false);
        Services.AddSingleton(gym);

        var context = new MemberContext();
        context.Set(new MemberDetail { Id = Guid.NewGuid(), FirstName = "Ana", LastName = "Prueba", Status = "Active" });
        Services.AddSingleton(context);

        // Pedir un servicio cierra el registro: el viewport se fija justo antes de renderizar.
        Services.GetRequiredService<ViewportState>().Pin(narrow);
        return RenderComponent<Home>();
    }

    [Fact]
    public void The_home_is_loaded_with_a_single_call_and_the_gym_pieces_are_not_requested_again()
    {
        var cut = RenderHome(FullHome(), narrow: true);

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#home-news")));
        Assert.Single(_me.Calls, c => c.Method == "GetMyHomeAsync");
        Assert.DoesNotContain(_me.Calls, c => c.Method is "HoursAsync" or "GetMyOccupancyAsync" or "RoutinesGETAsync");
    }

    [Fact]
    public void On_the_phone_the_blocks_come_in_a_single_column_in_the_spec_order()
    {
        var cut = RenderHome(FullHome(), narrow: true);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#home-news")));

        Assert.Empty(cut.FindAll(".gsm-m-home__side"));
        var markup = cut.Markup;
        var order = new[]
        {
            "gsm-m-hero", "gym-open-badge", "checkin-done", "home-offer", "home-training",
            "home-next-class", "home-progress", "home-news",
        };
        var positions = order.Select(marker => markup.IndexOf(marker, StringComparison.Ordinal)).ToArray();
        Assert.All(positions, p => Assert.True(p >= 0));
        Assert.Equal(positions.OrderBy(p => p), positions);
    }

    [Fact]
    public void On_desktop_the_left_column_holds_status_entry_and_training_and_the_right_the_rest()
    {
        var cut = RenderHome(FullHome(), narrow: false);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#home-news")));

        var main = cut.Find(".gsm-m-home__main");
        var side = cut.Find(".gsm-m-home__side");

        Assert.NotEmpty(main.QuerySelectorAll(".gsm-m-hero"));
        Assert.NotEmpty(main.QuerySelectorAll("#checkin-done"));
        Assert.NotEmpty(main.QuerySelectorAll("#home-training"));
        Assert.Empty(main.QuerySelectorAll("#home-next-class, #home-progress, #home-news, #gym-open-badge"));

        Assert.NotEmpty(side.QuerySelectorAll("#gym-open-badge"));
        Assert.NotEmpty(side.QuerySelectorAll("[data-testid='home-offer']"));
        Assert.NotEmpty(side.QuerySelectorAll("#home-next-class"));
        Assert.NotEmpty(side.QuerySelectorAll("#home-progress"));
        Assert.NotEmpty(side.QuerySelectorAll("#home-news"));
        Assert.Empty(side.QuerySelectorAll("#home-training, .gsm-m-hero"));
    }

    [Fact]
    public void The_hero_shows_the_commercial_status_and_the_expiry_notice_linking_to_my_membership()
    {
        var cut = RenderHome(FullHome(), narrow: true);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("POR VENCER", cut.Find(".gsm-m-hero__top").TextContent);
            var notice = cut.Find("#home-notice");
            Assert.Contains("Tu plan vence en 4 días", notice.TextContent);
            Assert.Equal("/app/membresia", notice.GetAttribute("href"));
        });
    }

    [Fact]
    public void Without_a_notice_the_hero_still_links_to_my_membership()
    {
        var home = FullHome();
        home.Status.Access.Notice = null;
        home.Status.Access.Commercial = "current";

        var cut = RenderHome(home, narrow: true);

        cut.WaitForAssertion(() => Assert.Equal("/app/membresia", cut.Find("#home-membership-link").GetAttribute("href")));
        Assert.Empty(cut.FindAll("#home-notice"));
    }

    [Fact]
    public void Having_entered_today_replaces_the_scan_card_with_the_time()
    {
        var cut = RenderHome(FullHome(), narrow: true);

        cut.WaitForAssertion(() =>
        {
            var done = cut.Find("#checkin-done").TextContent;
            Assert.Contains("Entraste hoy a las", done);
            Assert.Contains("06:12", done);
        });
        Assert.Empty(cut.FindAll(".gsm-m-checkin"));
    }

    [Fact]
    public void Without_an_entry_today_the_scan_card_stays()
    {
        var home = FullHome();
        home.Entry = new HomeEntryBlock { CheckedInToday = false };

        var cut = RenderHome(home, narrow: true);

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".gsm-m-checkin")));
        Assert.Empty(cut.FindAll("#checkin-done"));
    }

    [Fact]
    public void Pending_shows_the_waitlist_offer_with_its_deadline_and_the_reward_to_claim()
    {
        var cut = RenderHome(FullHome(), narrow: true);

        cut.WaitForAssertion(() =>
        {
            var offer = cut.Find("[data-testid='home-offer']");
            Assert.Contains("Spinning", offer.TextContent);
            Assert.Contains("confirma antes de las", offer.TextContent);
            Assert.Equal("/app/clases", offer.GetAttribute("href"));

            var reward = cut.Find("[data-testid='home-reward']");
            Assert.Contains("100 visitas", reward.TextContent);
            Assert.Contains("Una camiseta", reward.TextContent);
        });
    }

    [Fact]
    public void Training_shows_the_day_and_warns_when_the_review_is_near()
    {
        var cut = RenderHome(FullHome(), narrow: true);

        cut.WaitForAssertion(() =>
        {
            var training = cut.Find("#home-training");
            Assert.Contains("Pecho y tríceps", training.TextContent);
            Assert.Contains("6 ejercicios", training.TextContent);
            Assert.Contains("se revisa en 3 días", cut.Find("#home-review").TextContent);
        });
    }

    [Fact]
    public void A_rest_day_says_to_recover_and_a_far_review_is_not_mentioned()
    {
        var home = FullHome();
        home.Training.IsRest = true;
        home.Training.ReviewOn = null;
        home.Training.ReviewDaysLeft = null;

        var cut = RenderHome(home, narrow: true);

        cut.WaitForAssertion(() => Assert.Contains("Día de descanso", cut.Find("#home-training").TextContent));
        Assert.Empty(cut.FindAll("#home-review"));
    }

    [Theory]
    [InlineData(-2, "vencida")]
    [InlineData(0, "Hoy toca revisar")]
    [InlineData(1, "mañana")]
    [InlineData(5, "en 5 días")]
    public void The_review_text_depends_on_the_days_left(int daysLeft, string expected)
        => Assert.Contains(expected, Home.ReviewText(new DateOnly(2026, 6, 20), daysLeft));

    [Fact]
    public void Next_class_links_to_the_schedule()
    {
        var cut = RenderHome(FullHome(), narrow: true);

        cut.WaitForAssertion(() =>
        {
            var next = cut.Find("#home-next-class");
            Assert.Contains("Yoga", next.TextContent);
            Assert.Contains("18:30", next.TextContent);
            Assert.Contains("Laura", next.TextContent);
            Assert.Equal("/app/clases", next.GetAttribute("href"));
        });
    }

    [Fact]
    public void Progress_opens_my_progress_and_shows_the_ranking_position_when_participating()
    {
        var cut = RenderHome(FullHome(), narrow: true);

        cut.WaitForAssertion(() =>
        {
            var progress = cut.Find("#home-progress");
            Assert.Contains("9", progress.TextContent);
            Assert.Contains("Mi progreso", progress.TextContent);
            Assert.Contains("puesto 3", cut.Find("#home-ranking").TextContent);
            Assert.All(progress.QuerySelectorAll("a"), a => Assert.Equal("/app/logros", a.GetAttribute("href")));
        });
    }

    [Fact]
    public void Not_participating_in_the_ranking_shows_no_ranking_line()
    {
        var home = FullHome();
        home.Progress.Ranking = new HomeRankingStanding { Participating = false, Days = 0 };

        var cut = RenderHome(home, narrow: true);

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#home-progress")));
        Assert.Empty(cut.FindAll("#home-ranking"));
    }

    [Fact]
    public void News_shows_the_pinned_announcement_and_how_many_are_unread()
    {
        var cut = RenderHome(FullHome(), narrow: true);

        cut.WaitForAssertion(() =>
        {
            var news = cut.Find("#home-news");
            Assert.Contains("Cierre por festivo", news.TextContent);
            Assert.Contains("Fijado", news.TextContent);
            Assert.Contains("2 sin leer", cut.Find("#home-news-unread").TextContent);
            Assert.Equal("/app/novedades", news.GetAttribute("href"));
        });
    }

    [Fact]
    public void Null_blocks_are_not_painted_and_the_recent_visits_list_is_gone()
    {
        var home = new MemberHomeResponse { Status = FullHome().Status };

        var cut = RenderHome(home, narrow: true);

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".gsm-m-hero")));
        Assert.Empty(cut.FindAll("#home-training, #home-next-class, #home-progress, #home-news, #checkin-done"));
        Assert.DoesNotContain("Últimas visitas", cut.Markup);
    }
}
