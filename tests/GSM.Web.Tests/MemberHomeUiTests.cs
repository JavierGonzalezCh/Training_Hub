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
/// Hoy, el inicio del miembro: una sola llamada (<c>GetMyHomeAsync</c>); en el celular una columna (saludo, aviso de la
/// membresía, pendientes, entrada, entrenamiento, clase, gym ahora y progreso); en escritorio saludo, aviso, entrada y
/// entrenamiento a la izquierda y el resto a la derecha. Un bloque nulo no se pinta. El QR y las novedades no están aquí.
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
            RecentMedals =
            [
                new HomeMedal { AchievementId = Guid.NewGuid(), Name = "Hábito de Hierro", IconUrl = "/medals/habito.png", UnlockedAt = DateTimeOffset.UtcNow },
                new HomeMedal { AchievementId = Guid.NewGuid(), Name = "Imparable", IconUrl = "/medals/imparable.png", UnlockedAt = DateTimeOffset.UtcNow.AddDays(-1) },
            ],
            NextAchievement = new HomeNextAchievement
            {
                AchievementId = Guid.NewGuid(), Name = "Centurión", IconUrl = "/medals/centurion.png",
                ConditionType = "Visits", ConditionValue = 100, Progress = 98,
            },
        },
        News = new HomeNewsBlock
        {
            Featured = new HomeFeaturedAnnouncement { Id = Guid.NewGuid(), Title = "Cierre por festivo", Pinned = true },
            UnreadAnnouncements = 2,
        },
    };

    [Fact]
    public void The_medal_strip_shows_the_latest_medals_and_what_is_left_for_the_next_one()
    {
        var cut = RenderHome(FullHome(), narrow: true);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='home-medals']")));

        var medals = cut.FindAll("[data-testid='home-medal']");
        Assert.Equal(2, medals.Count);
        Assert.Equal("/medals/habito.png", medals[0].QuerySelector("img")!.GetAttribute("src"));

        var next = cut.Find("[data-testid='home-next-achievement']");
        Assert.Contains("Centurión", next.TextContent);
        Assert.Contains("Te faltan 2 visitas", next.TextContent);
        Assert.Equal("98", next.QuerySelector("[role='progressbar']")!.GetAttribute("aria-valuenow"));

        // La franja va arriba, antes del aviso de membresía y del entrenamiento; ya no dentro de «Tu progreso».
        var markup = cut.Markup;
        Assert.True(markup.IndexOf("home-medals", StringComparison.Ordinal) < markup.IndexOf("home-training", StringComparison.Ordinal));
    }

    [Fact]
    public void Pending_consents_come_right_after_the_greeting_before_everything_else()
    {
        var cut = RenderHome(FullHome(), narrow: true, pendingConsents: 2);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".gsm-m-pending-consents")));

        var markup = cut.Markup;
        var consents = markup.IndexOf("gsm-m-pending-consents", StringComparison.Ordinal);
        Assert.True(markup.IndexOf("gsm-m-hero", StringComparison.Ordinal) < consents);
        Assert.True(consents < markup.IndexOf("home-notice", StringComparison.Ordinal));
        Assert.True(consents < markup.IndexOf("home-news", StringComparison.Ordinal));
    }

    private IRenderedComponent<Home> RenderHome(MemberHomeResponse? home, bool narrow, bool withPlan = false, int pendingConsents = 0)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddScoped<ViewportState>();

        var me = DispatchProxy.Create<IMeClient, FakeClient>();
        _me = (FakeClient)(object)me;
        _me.Handlers["GetMyHomeAsync"] = _ => home;
        var consents = DispatchProxy.Create<IMeConsentsClient, FakeClient>();
        ((FakeClient)(object)consents).Handlers["ListMyConsentsAsync"] = _ => Enumerable.Range(0, pendingConsents)
            .Select(_ => new MemberConsentStatus { Status = "pending" }).ToList();
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
        context.Set(new MemberDetail
        {
            Id = Guid.NewGuid(), FirstName = "Ana", LastName = "Prueba", Status = "Active",
            CurrentMembership = withPlan
                ? new MemberCurrentMembership { PlanName = "Mensual", StartDate = new DateOnly(2026, 6, 1), EndDate = new DateOnly(2026, 6, 30) }
                : null,
        });
        Services.AddSingleton(context);

        // Pedir un servicio cierra el registro: el viewport se fija justo antes de renderizar.
        Services.GetRequiredService<ViewportState>().Pin(narrow);
        return RenderComponent<Home>();
    }

    [Fact]
    public void The_home_is_loaded_with_a_single_call_and_the_gym_pieces_are_not_requested_again()
    {
        var cut = RenderHome(FullHome(), narrow: true);

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#home-next-class")));
        Assert.Single(_me.Calls, c => c.Method == "GetMyHomeAsync");
        Assert.DoesNotContain(_me.Calls, c => c.Method is "HoursAsync" or "GetMyOccupancyAsync" or "RoutinesGETAsync");
    }

    [Fact]
    public void On_the_phone_the_blocks_come_in_a_single_column_in_the_spec_order()
    {
        var cut = RenderHome(FullHome(), narrow: true);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#home-next-class")));

        Assert.Empty(cut.FindAll(".gsm-m-home__side"));
        var markup = cut.Markup;
        var order = new[]
        {
            "gsm-m-hero", "gym-open-badge", "home-notice", "home-news", "home-offer", "checkin-done", "home-training",
            "home-next-class",
        };
        var positions = order.Select(marker => markup.IndexOf(marker, StringComparison.Ordinal)).ToArray();
        Assert.All(positions, p => Assert.True(p >= 0));
        Assert.Equal(positions.OrderBy(p => p), positions);
    }

    [Fact]
    public void On_desktop_the_left_column_holds_status_entry_and_training_and_the_right_the_rest()
    {
        var cut = RenderHome(FullHome(), narrow: false);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#home-next-class")));

        var main = cut.Find(".gsm-m-home__main");
        var side = cut.Find(".gsm-m-home__side");

        Assert.NotEmpty(main.QuerySelectorAll(".gsm-m-hero"));
        Assert.NotEmpty(main.QuerySelectorAll("#home-notice"));
        Assert.NotEmpty(main.QuerySelectorAll("#checkin-done"));
        Assert.NotEmpty(main.QuerySelectorAll("#home-training"));
        Assert.Empty(main.QuerySelectorAll("#home-next-class"));

        // El gym ahora va dentro del saludo, no en su propia tarjeta.
        Assert.NotEmpty(main.QuerySelectorAll(".gsm-m-hero #gym-open-badge"));
        Assert.NotEmpty(side.QuerySelectorAll("[data-testid='home-offer']"));
        Assert.NotEmpty(side.QuerySelectorAll("#home-next-class"));
        Assert.Empty(side.QuerySelectorAll("#home-training, .gsm-m-hero"));
    }

    [Fact]
    public void The_hero_shows_the_commercial_status_and_the_expiry_notice_is_an_actionable_renew_card()
    {
        var cut = RenderHome(FullHome(), narrow: true);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("POR VENCER", cut.Find(".gsm-m-hero__top").TextContent);
            var notice = cut.Find("#home-notice");
            Assert.Contains("Tu plan vence en 4 días", notice.TextContent);
            Assert.Contains("Renovar", notice.TextContent);
            Assert.Equal("/app/membresia", notice.GetAttribute("href"));
        });
    }

    [Fact]
    public void Without_a_notice_and_with_a_current_plan_the_membership_takes_no_space()
    {
        var home = FullHome();
        home.Status.Access.Notice = null;
        home.Status.Access.Commercial = "current";

        var cut = RenderHome(home, narrow: true, withPlan: true);

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#home-training")));
        Assert.Empty(cut.FindAll("#home-notice, #home-no-plan"));
    }

    [Fact]
    public void Without_a_plan_the_card_points_to_my_membership()
    {
        var home = FullHome();
        home.Status.Access.Notice = null;

        var cut = RenderHome(home, narrow: true);

        cut.WaitForAssertion(() => Assert.Equal("/app/membresia", cut.Find("#home-no-plan").GetAttribute("href")));
    }

    [Fact]
    public void Having_entered_today_shows_the_time()
    {
        var cut = RenderHome(FullHome(), narrow: true);

        cut.WaitForAssertion(() =>
        {
            var done = cut.Find("#checkin-done").TextContent;
            Assert.Contains("Entraste hoy a las", done);
            Assert.Contains("06:12", done);
        });
    }

    [Fact]
    public void Without_an_entry_today_there_is_no_scan_card_because_the_qr_is_in_the_header()
    {
        var home = FullHome();
        home.Entry = new HomeEntryBlock { CheckedInToday = false };

        var cut = RenderHome(home, narrow: true);

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#home-training")));
        Assert.Empty(cut.FindAll(".gsm-m-checkin, #checkin-done"));
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
    public void The_progress_card_is_gone_and_the_streak_shows_once_even_without_planned_days()
    {
        var cut = RenderHome(FullHome(), narrow: true);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#home-next-class")));

        Assert.Empty(cut.FindAll("#home-progress"));
        // Sin días planeados no hay anillo: la racha va sola.
        var streak = cut.Find("[data-testid='home-streak']");
        Assert.Contains("9", streak.TextContent);
        Assert.Equal("/app/progreso", streak.GetAttribute("href"));
    }

    [Fact]
    public void Pinned_or_unread_news_is_one_card_with_the_unread_count_that_opens_the_news()
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
        Assert.Single(cut.FindAll("#home-news"));
    }

    [Fact]
    public void News_already_read_and_not_pinned_does_not_take_space_on_today()
    {
        var home = FullHome();
        home.News.Featured.Pinned = false;
        home.News.UnreadAnnouncements = 0;

        var cut = RenderHome(home, narrow: true);

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#home-training")));
        Assert.Empty(cut.FindAll("#home-news"));
    }

    [Fact]
    public void The_training_card_carries_the_main_action_start_training()
    {
        var cut = RenderHome(FullHome(), narrow: true);

        cut.WaitForAssertion(() =>
        {
            var start = cut.Find("[data-testid='home-start']");
            Assert.Contains("Empezar a entrenar", start.TextContent);
            Assert.Equal("/app/rutina", start.GetAttribute("href"));
        });
    }

    [Fact]
    public void The_gym_tab_shows_the_pinned_news_with_unread_and_the_booked_class()
    {
        var cut = RenderHome(FullHome(), narrow: true);
        _me.Handlers["GymGET2Async"] = _ => new MemberGymResponse { Name = "Forja", PrimaryColor = "#B50016", SecondaryColor = "#000000" };
        _me.Handlers["HoursAsync"] = _ => new MemberGymHoursResponse { Status = new GymOpenStatusDto() };

        var gym = RenderComponent<MyGym>();

        gym.WaitForAssertion(() =>
        {
            var news = gym.Find("#gym-news");
            Assert.Contains("Cierre por festivo", news.TextContent);
            Assert.Contains("Fijado", news.TextContent);
            Assert.Contains("2 sin leer", gym.Find("#gym-news-unread").TextContent);
            Assert.Equal("/app/novedades", news.GetAttribute("href"));
            Assert.Contains("Yoga", gym.Find("#gym-classes").TextContent);
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
