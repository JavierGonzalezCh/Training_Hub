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

/// <summary>SP1: el miembro puede ver y hacer cualquier día de su plan; se registra con la fecha de hoy.</summary>
public class RoutineOtherDayUiTests : TestContext
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

    private sealed class FakeApi(bool todayIsRest, string historyJson) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            Requests.Add((request.Method, path));

            string json;
            if (request.Method == HttpMethod.Post && path.Contains("/log"))
            {
                json = JsonSerializer.Serialize(new
                {
                    id = Guid.NewGuid(), routineExerciseId = ChestLine, performedOn = "2026-11-03",
                    completedAt = "2026-11-03T12:00:00Z", exerciseId = Guid.NewGuid(),
                });
            }
            else if (path.StartsWith("/api/me/routines/history"))
            {
                json = historyJson;
            }
            else if (path.StartsWith("/api/me/routines"))
            {
                var chest = Line(ChestLine, "Press de banca");
                var legs = Line(LegLine, "Sentadilla");
                json = JsonSerializer.Serialize(new
                {
                    routine = new
                    {
                        id = Guid.NewGuid(), memberId = Guid.NewGuid(), name = "Fuerza", assignedBy = Guid.NewGuid(),
                        startsOn = "2026-10-01", isActive = true, cycleLength = 7, scheduleMode = "weekly",
                        days = new object[]
                        {
                            new { id = ChestDay, position = 0, name = "Pecho", isRest = false, dayOfWeek = 1, exercises = new[] { chest } },
                            new { id = LegDay, position = 0, name = "Pierna", isRest = false, dayOfWeek = 2, exercises = new[] { legs } },
                        },
                    },
                    // Martes 3 nov: toca Pierna, o descanso si el test lo pide (jueves sin fila).
                    today = todayIsRest
                        ? (object)new { date = "2026-11-05", position = 0, dayName = "Descanso", isRest = true, dayOfWeek = 4, exercises = Array.Empty<object>() }
                        : new { date = "2026-11-03", position = 0, dayName = "Pierna", isRest = false, dayOfWeek = 2, exercises = new[] { legs } },
                });
            }
            else
            {
                json = "[]";
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    private FakeApi _api = null!;

    private IRenderedComponent<Routine> Render(bool todayIsRest = false, string history = "[]")
    {
        _api = new FakeApi(todayIsRest, history);
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
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
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='day-picker']")));
        return cut;
    }

    [Fact]
    public void A_training_day_shows_today_and_offers_the_other_days_of_the_plan()
    {
        var cut = Render();

        Assert.Contains("Hoy · Pierna", cut.Find("[data-testid='day-today']").TextContent);
        Assert.Contains("Pecho", cut.Find($"[data-testid='day-{ChestDay}']").TextContent);
        Assert.Empty(cut.FindAll($"[data-testid='day-{LegDay}']")); // el de hoy no se repite
        Assert.NotEmpty(cut.FindAll($"[data-testid='ex-{LegLine}']"));
        Assert.Empty(cut.FindAll("[data-testid='other-day-notice']"));
    }

    [Fact]
    public void Choosing_another_day_shows_its_exercises_and_logs_them_today()
    {
        var cut = Render();

        cut.Find($"[data-testid='day-{ChestDay}']").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll($"[data-testid='ex-{ChestLine}']")));
        Assert.Empty(cut.FindAll($"[data-testid='ex-{LegLine}']"));
        var notice = cut.Find("[data-testid='other-day-notice']").TextContent;
        Assert.Contains("Pecho", notice);
        Assert.Contains("pregúntale a tu entrenador", notice);

        cut.Find(".gsm-m-ex__head").Click();
        cut.Find(".gsm-m-setrow input").Input("40");
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Guardar").Click();

        cut.WaitForAssertion(() => Assert.Single(_api.Requests,
            r => r.Method == HttpMethod.Post && r.Path == $"/api/routine-exercises/{ChestLine}/log"));
    }

    [Fact]
    public void On_a_rest_day_the_member_can_pick_any_training_day()
    {
        var cut = Render(todayIsRest: true);

        Assert.Contains("Hoy descansas", cut.Markup);
        Assert.Empty(cut.FindAll("[data-testid='day-today']"));
        Assert.Empty(cut.FindAll(".gsm-m-ex"));

        cut.Find($"[data-testid='day-{LegDay}']").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll($"[data-testid='ex-{LegLine}']")));
    }

    [Fact]
    public void What_was_logged_today_from_another_day_reopens_that_day()
    {
        var history = JsonSerializer.Serialize(new[]
        {
            new
            {
                id = Guid.NewGuid(), routineExerciseId = ChestLine, exerciseId = Guid.NewGuid(), exerciseName = "Press de banca",
                performedOn = "2026-11-03", completedAt = "2026-11-03T12:00:00Z",
                sets = new[] { new { number = 1, setType = "normal", reps = 10, weightKg = 40.0 } },
            },
        });

        var cut = Render(history: history);

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll($"[data-testid='ex-{ChestLine}'].is-done")));
        Assert.NotEmpty(cut.FindAll("[data-testid='other-day-notice']"));
    }
}
