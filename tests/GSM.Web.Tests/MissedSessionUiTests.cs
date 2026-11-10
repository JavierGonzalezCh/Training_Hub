using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using GSM.Client.Generated;
using GSM.Member.UI.Pages;
using GSM.UI.Shared.Auth;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>SP3–SP4: la tarjeta «Ayer te faltó…», el diagrama antes/después y la recuperación.</summary>
public class MissedSessionUiTests : TestContext
{
    private static readonly Guid ChestDay = Guid.Parse("00000000-0000-0000-0000-0000000000d1");
    private static readonly Guid LegDay = Guid.Parse("00000000-0000-0000-0000-0000000000d2");
    private static readonly Guid ChestLine = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
    private static readonly Guid LegLine = Guid.Parse("00000000-0000-0000-0000-0000000000c2");

    private static object Line(Guid id, string name) => new
    {
        id, exerciseId = Guid.NewGuid(), exerciseName = name, order = 1,
        setTargets = new object[] { new { number = 1, setType = "normal", repsMin = 10, repsMax = 10 } },
        restSeconds = 60, trackingType = "RepsWeight", isUnilateral = false, muscleGroup = "UpperBody",
    };

    private static object Slot(string date, int dow, bool rest, Guid? dayId, string name)
        => new { date, dayOfWeek = dow, isRest = rest, dayId, dayName = name };

    private sealed class FakeApi(bool todayIsRest, bool weekly) : HttpMessageHandler
    {
        public bool Recovered { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            string json;
            if (request.Method == HttpMethod.Post && path == "/api/me/routines/missed-session")
            {
                Body = await request.Content!.ReadAsStringAsync(cancellationToken);
                Recovered = true;
                json = Routine();
            }
            else if (path.StartsWith("/api/me/routines/history"))
            {
                json = "[]";
            }
            else if (path.StartsWith("/api/me/routines"))
            {
                json = Routine();
            }
            else
            {
                json = "[]";
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }

        private string Routine()
        {
            var chest = Line(ChestLine, "Press de banca");
            var legs = Line(LegLine, "Sentadilla");
            object today = todayIsRest
                ? new { date = "2026-11-05", position = 0, dayName = "Descanso", isRest = true, dayOfWeek = 4, exercises = Array.Empty<object>() }
                : Recovered
                    ? new { date = "2026-11-03", position = 0, dayName = "Pecho", isRest = false, dayOfWeek = 2, exercises = new[] { chest }, dayId = (Guid?)ChestDay, plannedDayName = (string?)"Pierna" }
                    : new { date = "2026-11-03", position = 0, dayName = "Pierna", isRest = false, dayOfWeek = 2, exercises = new[] { legs }, dayId = (Guid?)LegDay, plannedDayName = (string?)null };

            var changedWeek = new[] { Slot("2026-11-03", 2, false, ChestDay, "Pecho"), Slot("2026-11-04", 3, true, null, "Descanso") };
            return JsonSerializer.Serialize(new
            {
                routine = new
                {
                    id = Guid.NewGuid(), memberId = Guid.NewGuid(), name = "Fuerza", assignedBy = Guid.NewGuid(),
                    startsOn = "2026-10-01", isActive = true, cycleLength = 7, scheduleMode = weekly ? "weekly" : "rotating",
                    days = new object[]
                    {
                        new { id = ChestDay, position = 1, name = "Pecho", isRest = false, dayOfWeek = weekly ? 1 : (int?)null, exercises = new[] { chest } },
                        new { id = LegDay, position = 2, name = "Pierna", isRest = false, dayOfWeek = weekly ? 2 : (int?)null, exercises = new[] { legs } },
                    },
                },
                today,
                missedSession = Recovered ? null : new
                {
                    missedOn = "2026-11-02",
                    missedDayId = ChestDay,
                    missedDayName = "Pecho",
                    todayIsRest,
                    week = new[] { Slot("2026-11-03", 2, false, LegDay, "Pierna"), Slot("2026-11-04", 3, true, null, "Descanso") },
                    isRotating = !weekly,
                    swap = todayIsRest ? null : new { week = changedWeek, lostDayName = (string?)"Pierna", shiftsCycle = false },
                    reorganize = todayIsRest ? null : new { week = changedWeek, lostDayName = weekly ? (string?)"Hombro" : null, shiftsCycle = !weekly },
                    trainerName = "Laura",
                },
            });
        }
    }

    private FakeApi _api = null!;

    private IRenderedComponent<Routine> Render(bool todayIsRest = false, bool weekly = true)
    {
        _api = new FakeApi(todayIsRest, weekly);
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<GSM.Client.Http.SessionEvents>();
        var http = new HttpClient(_api) { BaseAddress = new Uri("http://localhost") };
        Services.AddSingleton<IExercisesClient>(new ExercisesClient(http));
        Services.AddSingleton<IMeClient>(new MeClient(http));
        Services.AddSingleton<IRoutineExercisesClient>(new RoutineExercisesClient(http));
        Services.AddSingleton<GymContext>();
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("valentina");
        auth.SetRoles("member");
        RenderComponent<MudPopoverProvider>();
        var cut = RenderComponent<Routine>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='missed-session']")));
        return cut;
    }

    private static void Click(IRenderedComponent<Routine> cut, string testId)
    {
        var target = cut.Find($"[data-testid='{testId}']");
        (target.TagName == "BUTTON" ? target : target.QuerySelector("button") ?? target).Click();
    }

    [Fact]
    public void A_training_day_asks_and_keeping_today_hides_the_card()
    {
        var cut = Render();

        var card = cut.Find("[data-testid='missed-session']").TextContent;
        Assert.Contains("Ayer te faltó Pecho", card);
        Assert.Contains("pregúntale a tu entrenador (Laura)", card);

        Click(cut, "missed-keep-today");

        Assert.Empty(cut.FindAll("[data-testid='missed-session']"));
        Assert.False(_api.Recovered);
    }

    [Fact]
    public void Doing_the_missed_one_shows_both_weeks_before_deciding_and_reorganizing_saves_it()
    {
        var cut = Render();

        Click(cut, "missed-do");

        Assert.Equal(2, cut.FindAll("[data-testid='week-diagram']").Count);
        Assert.NotEmpty(cut.FindAll("[data-testid='diagram-changed']"));
        Assert.Contains("Hombro", cut.Find("[data-testid='missed-option-reorganize']").TextContent);

        Click(cut, "missed-confirm-reorganize");

        cut.WaitForAssertion(() => Assert.True(_api.Recovered));
        Assert.Contains("\"reorganize\":true", _api.Body!.Replace(" ", ""));
        Assert.Contains(ChestDay.ToString(), _api.Body);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='today-changed']")));
        Assert.Contains("en lugar de Pierna", cut.Find("[data-testid='today-changed']").TextContent);
        Assert.NotEmpty(cut.FindAll($"[data-testid='ex-{ChestLine}']"));
        Assert.Empty(cut.FindAll("[data-testid='missed-session']"));
    }

    [Fact]
    public void A_rotating_routine_offers_to_shift_the_whole_cycle_without_losing_any()
    {
        var cut = Render(weekly: false);

        Click(cut, "missed-do");

        var option = cut.Find("[data-testid='missed-option-reorganize']").TextContent;
        Assert.Contains("Correr toda la rutina", option);
        Assert.Contains("No se pierde ninguna rutina", option);
        Click(cut, "missed-confirm-reorganize");
        cut.WaitForAssertion(() => Assert.True(_api.Recovered));
        Assert.Contains("\"reorganize\":true", _api.Body!.Replace(" ", ""));
    }

    [Fact]
    public void On_a_rest_day_it_suggests_and_opens_the_missed_day_without_saving()
    {
        var cut = Render(todayIsRest: true);

        Click(cut, "missed-do-on-rest");

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll($"[data-testid='ex-{ChestLine}']")));
        Assert.False(_api.Recovered);
    }
}
