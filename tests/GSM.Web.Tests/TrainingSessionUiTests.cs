using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using GSM.Client.Generated;
using GSM.Member.UI;
using GSM.Member.UI.Pages;
using GSM.UI.Shared.Auth;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>
/// Modo sesión de Entrenar: un ejercicio por pantalla con las series ya llenas, cada ✓ guarda las series hechas, el
/// descanso lo inicia el miembro y al terminar sale el resumen. También el mapa muscular de la app.
/// </summary>
public class TrainingSessionUiTests : TestContext
{
    private static readonly Guid Day = Guid.Parse("00000000-0000-0000-0000-0000000000d1");
    private static readonly Guid SquatLine = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
    private static readonly Guid ThrustLine = Guid.Parse("00000000-0000-0000-0000-0000000000c2");
    private static readonly Guid SquatId = Guid.Parse("00000000-0000-0000-0000-0000000000e1");

    private static object Line(Guid id, Guid exerciseId, string name, string primary, string[] secondary) => new
    {
        id, exerciseId, exerciseName = name, order = 1,
        setTargets = new object[]
        {
            new { number = 1, setType = "normal", repsMin = 8, repsMax = 10, weightKg = 60 },
            new { number = 2, setType = "normal", repsMin = 8, repsMax = 10, weightKg = 60 },
        },
        restSeconds = 90, trackingType = "RepsWeight", isUnilateral = false, muscleGroup = "LowerBody",
        primaryMuscle = primary, secondaryMuscles = secondary,
    };

    private sealed class FakeApi : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, path, body));

            string json;
            if (request.Method == HttpMethod.Post && path.Contains("/log"))
            {
                json = JsonSerializer.Serialize(new
                {
                    id = Guid.NewGuid(), routineExerciseId = SquatLine, performedOn = "2026-11-06",
                    completedAt = "2026-11-06T12:00:00Z", exerciseId = SquatId,
                });
            }
            else if (path.StartsWith("/api/me/progress"))
            {
                json = JsonSerializer.Serialize(new[]
                {
                    new
                    {
                        exerciseId = SquatId, exerciseName = "Sentadilla", trackingType = "RepsWeight", isUnilateral = false,
                        sessions = 3, lastPerformedOn = "2026-11-01",
                        records = new[] { new { kind = "best_weight", value = 70, unit = "kg", achievedOn = "2026-11-01" } },
                    },
                });
            }
            else if (path.StartsWith("/api/me/routines/history"))
            {
                json = "[]";
            }
            else if (path.StartsWith("/api/me/routines"))
            {
                var squat = Line(SquatLine, SquatId, "Sentadilla", "Quadriceps", ["Glutes", "Hamstrings"]);
                var thrust = Line(ThrustLine, Guid.NewGuid(), "Hip thrust", "Glutes", ["Hamstrings"]);
                json = JsonSerializer.Serialize(new
                {
                    routine = new
                    {
                        id = Guid.NewGuid(), memberId = Guid.NewGuid(), name = "Fuerza", assignedBy = Guid.NewGuid(),
                        startsOn = "2026-10-01", isActive = true, cycleLength = 7, scheduleMode = "weekly",
                        days = new object[] { new { id = Day, position = 0, name = "Pierna", isRest = false, dayOfWeek = 5, exercises = new[] { squat, thrust } } },
                    },
                    today = new { date = "2026-11-06", position = 0, dayName = "Pierna", isRest = false, dayOfWeek = 5, dayId = Day, exercises = new[] { squat, thrust } },
                });
            }
            else
            {
                json = "[]";
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private FakeApi _api = null!;

    private IRenderedComponent<Routine> Render()
    {
        _api = new FakeApi();
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
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='start-day']")));
        return cut;
    }

    [Fact]
    public void Before_starting_the_day_shows_what_it_works_and_each_exercise_has_its_photo()
    {
        var cut = Render();

        var worked = cut.Find("[data-testid='today-muscles']");
        Assert.Contains("Cuádriceps y glúteos", worked.TextContent);
        Assert.Contains("isquiotibiales", worked.TextContent);
        Assert.Equal(2, cut.FindAll(".gsm-m-ex__thumb").Count);
        Assert.Contains("Empezar Pierna", cut.Find("[data-testid='start-day']").TextContent);
    }

    [Fact]
    public void Starting_opens_one_exercise_per_screen_with_the_sets_prefilled()
    {
        var cut = Render();

        cut.Find("[data-testid='start-day']").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='session']")));
        Assert.Contains("Ejercicio 1 de 2", cut.Find("[data-testid='session-position']").TextContent);
        Assert.Contains("Sentadilla", cut.Find("[data-testid='session-exercise']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='start-day']"));
        var weight = cut.Find("[data-testid='session-set-0'] input");
        Assert.Equal("60", weight.GetAttribute("value"));
    }

    [Fact]
    public void Each_check_saves_the_sets_done_so_far()
    {
        var cut = Render();
        cut.Find("[data-testid='start-day']").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='session-check-0']")));

        cut.Find("[data-testid='session-check-0']").Click();
        cut.WaitForAssertion(() => Assert.Single(_api.Requests, r => r.Method == HttpMethod.Post && r.Path.Contains("/log")));
        var first = _api.Requests.Last(r => r.Method == HttpMethod.Post).Body!;
        Assert.Single(JsonDocument.Parse(first).RootElement.GetProperty("sets").EnumerateArray());

        cut.Find("[data-testid='session-check-1']").Click();
        cut.WaitForAssertion(() => Assert.Equal(2, _api.Requests.Count(r => r.Method == HttpMethod.Post && r.Path.Contains("/log"))));
        var second = _api.Requests.Last(r => r.Method == HttpMethod.Post).Body!;
        Assert.Equal(2, JsonDocument.Parse(second).RootElement.GetProperty("sets").GetArrayLength());
        Assert.Contains("is-done", cut.Find("[data-testid='session-set-0']").ClassName);
    }

    [Fact]
    public void The_rest_timer_starts_only_when_the_member_asks()
    {
        var cut = Render();
        cut.Find("[data-testid='start-day']").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='session-rest-start']")));

        Assert.Empty(cut.FindAll("[data-testid='session-rest']"));
        Assert.Contains("90 s", cut.Find("[data-testid='session-rest-start']").TextContent);
        cut.Find("[data-testid='session-rest-start']").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='session-rest']")));
    }

    [Fact]
    public void Moving_to_the_last_exercise_and_finishing_shows_the_summary()
    {
        var cut = Render();
        cut.Find("[data-testid='start-day']").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='session-check-0']")));
        cut.Find("[data-testid='session-check-0']").Click();
        cut.WaitForAssertion(() => Assert.Contains("is-done", cut.Find("[data-testid='session-set-0']").ClassName));

        cut.Find("[data-testid='session-next']").Click();
        Assert.Contains("Hip thrust", cut.Find("[data-testid='session-exercise']").TextContent);
        cut.Find("[data-testid='session-finish']").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='session-summary']")));
        var summary = cut.Find("[data-testid='session-summary']").TextContent;
        Assert.Contains("1/2", summary);
        Assert.Contains("Te faltaron 1 ejercicio", summary);
    }

    [Fact]
    public void The_muscle_map_colors_primary_and_secondary_muscles()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var cut = RenderComponent<MuscleMap>(p => p
            .Add(x => x.Primary, ["Quadriceps"])
            .Add(x => x.Secondary, ["Glutes"]));

        var style = cut.Find("svg").GetAttribute("style")!;
        Assert.Contains("--m-quadriceps:" + MuscleMap.Hot, style);
        Assert.Contains("--m-glutes:" + MuscleMap.Warm, style);
    }

    [Fact]
    public void The_recovery_map_paints_tired_and_recovering_muscles_and_leaves_ready_ones_gray()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var cut = RenderComponent<MuscleMap>(p => p
            .Add(x => x.Recovery, new Dictionary<string, int> { ["Chest"] = 0, ["Triceps"] = 60, ["Quadriceps"] = 100 }));

        var style = cut.Find("svg").GetAttribute("style")!;
        Assert.Contains("--m-chest:" + MuscleMap.Hot, style);
        Assert.Contains("--m-triceps:" + MuscleMap.Warm, style);
        Assert.DoesNotContain("quadriceps", style);
    }

    [Fact]
    public void The_member_app_hides_the_rir_and_groups_sets_that_only_differ_in_it()
    {
        var targets = new[]
        {
            new SetTargetResponse { Number = 1, SetType = "normal", RepsMin = 8, RepsMax = 8, WeightKg = 80, Rir = 2 },
            new SetTargetResponse { Number = 2, SetType = "normal", RepsMin = 8, RepsMax = 8, WeightKg = 80, Rir = 1 },
        };

        Assert.Equal("2 × 8 · 80 kg", GSM.UI.Shared.Routines.SetPlan.Summary(targets, withRir: false));
        Assert.Contains("RIR 2", GSM.UI.Shared.Routines.SetPlan.Summary(targets));
    }

    [Fact]
    public void Muscle_names_are_joined_in_spanish()
        => Assert.Equal("cuádriceps, glúteos y lumbar", MuscleLabels.Join(["Quadriceps", "Glutes", "Lower_back"]));
}
