using MudBlazor;
using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using GSM.Client.Generated;
using GSM.Member.UI.Pages;
using GSM.UI.Shared.Auth;
using GSM.UI.Shared.Routines;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>R5: "Cambiar por…" en el editor de rutinas y "Hacer otro ejercicio hoy" en la app del miembro.</summary>
public class ExerciseAlternativesUiTests : TestContext
{
    private static readonly Guid PlannedId = Guid.Parse("00000000-0000-0000-0000-0000000000e1");
    private static readonly Guid RecommendedId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid SuggestedId = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
    private static readonly Guid LineId = Guid.Parse("00000000-0000-0000-0000-0000000000c1");

    private static object Alt(Guid id, string name, string source, string tracking, bool available) => new
    {
        id,
        name,
        source,
        primaryMuscle = "Quadriceps",
        muscleGroup = "LowerBody",
        exerciseType = "Strength",
        trackingType = tracking,
        isUnilateral = false,
        difficulty = "Intermediate",
        isAvailableHere = available,
        machines = Array.Empty<object>(),
    };

    private sealed class FakeApi : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.PathAndQuery;
            Requests.Add((request.Method, path, body));

            string json;
            if (path.StartsWith($"/api/exercises/{PlannedId}/alternatives"))
            {
                json = JsonSerializer.Serialize(new[]
                {
                    Alt(RecommendedId, "Prensa de piernas", "recommended", "RepsWeight", false),
                    Alt(SuggestedId, "Plancha lateral", "suggested", "Time", true),
                });
            }
            else if (request.Method == HttpMethod.Post && path.Contains("/log"))
            {
                json = JsonSerializer.Serialize(new
                {
                    id = Guid.NewGuid(), routineExerciseId = LineId, performedOn = "2026-10-15",
                    completedAt = "2026-10-15T12:00:00Z", exerciseId = SuggestedId,
                });
            }
            else if (path.StartsWith("/api/me/routines/history"))
            {
                json = "[]";
            }
            else if (path.StartsWith("/api/me/routines"))
            {
                var line = new
                {
                    id = LineId, exerciseId = PlannedId, exerciseName = "Sentadilla con barra", order = 1,
                    setTargets = new object[] { new { number = 1, setType = "normal", repsMin = 10, repsMax = 10 } },
                    restSeconds = 60, trackingType = "RepsWeight", isUnilateral = false, muscleGroup = "LowerBody",
                };
                json = JsonSerializer.Serialize(new
                {
                    routine = new
                    {
                        id = Guid.NewGuid(), memberId = Guid.NewGuid(), name = "Rutina R5", assignedBy = Guid.NewGuid(),
                        startsOn = "2026-10-01", isActive = true, cycleLength = 1, scheduleMode = "rotating",
                        days = new[] { new { id = Guid.NewGuid(), position = 1, name = "D1", isRest = false, exercises = new[] { line } } },
                    },
                    today = new
                    {
                        date = "2026-10-15", position = 1, dayName = "D1", isRest = false, dayOfWeek = 4,
                        exercises = new[] { line },
                    },
                });
            }
            else
            {
                json = "[]";
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private readonly FakeApi _api = new();

    public ExerciseAlternativesUiTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var http = new HttpClient(_api) { BaseAddress = new Uri("http://localhost") };
        Services.AddSingleton<IExercisesClient>(new ExercisesClient(http));
        Services.AddSingleton<IMeClient>(new MeClient(http));
        Services.AddSingleton<IRoutineExercisesClient>(new RoutineExercisesClient(http));
        Services.AddSingleton<GymContext>();
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("luis");
        auth.SetRoles("staff", "member");
    }

    private static RoutineDraft DraftWithExistingLine()
    {
        var draft = RoutineDraft.Empty();
        draft.Name = "Rutina R5";
        draft.Days[0].Exercises.Add(new DraftExercise
        {
            Id = LineId,
            Exercise = new ExerciseResponse { Id = PlannedId, Name = "Sentadilla con barra", TrackingType = "RepsWeight", MuscleGroup = "LowerBody" },
        });
        return draft;
    }

    // --- Editor: "Cambiar por…" ---

    [Fact]
    public void Change_for_lists_recommended_first_and_flags_what_the_gym_cannot_do_today()
    {
        RenderComponent<MudPopoverProvider>();
        var cut = RenderComponent<RoutineForm>(p => p.Add(x => x.Draft, DraftWithExistingLine()));

        cut.Find("[data-testid='change-0-0']").Click();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid^='line-0-0-alt-']").Count));
        var items = cut.FindAll("[data-testid^='line-0-0-alt-']");
        Assert.Contains("Prensa de piernas", items[0].TextContent);
        Assert.Contains("Recomendada", cut.Find($"[data-testid='line-0-0-source-{RecommendedId}']").TextContent);
        Assert.Contains("Sugerida", cut.Find($"[data-testid='line-0-0-source-{SuggestedId}']").TextContent);
        Assert.NotNull(cut.Find($"[data-testid='line-0-0-unavailable-{RecommendedId}']"));
        Assert.Empty(cut.FindAll($"[data-testid='line-0-0-unavailable-{SuggestedId}']"));
    }

    [Fact]
    public void Picking_an_alternative_puts_it_in_a_new_line_and_the_old_one_is_left_out_of_the_request()
    {
        var draft = DraftWithExistingLine();
        var line = draft.Days[0].Exercises[0];
        line.RestSeconds = 90;
        line.Notes = "Cuidar la espalda";
        RenderComponent<MudPopoverProvider>();
        var cut = RenderComponent<RoutineForm>(p => p.Add(x => x.Draft, draft));

        cut.Find("[data-testid='change-0-0']").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll($"[data-testid='line-0-0-pick-{SuggestedId}']")));
        cut.Find($"[data-testid='line-0-0-pick-{SuggestedId}']").Click();

        Assert.Equal(SuggestedId, line.Exercise!.Id);
        Assert.Null(line.Id); // línea nueva: la anterior se retira en el servidor y conserva su historial
        Assert.Equal((90, "Cuidar la espalda"), (line.RestSeconds, line.Notes));
        Assert.True(line.Fields.Time);              // el ejercicio nuevo mide tiempo: las series de partida cambian
        Assert.Empty(cut.FindAll("[data-testid='line-0-0-alternatives']"));

        var sent = Assert.Single(draft.ToRequest().Days.Single().Exercises);
        Assert.Equal(SuggestedId, sent.ExerciseId);
        Assert.Null(sent.Id);
    }

    // --- App del miembro: "Hacer otro ejercicio hoy" ---

    [Fact]
    public void Doing_another_exercise_today_logs_the_replacement_and_keeps_the_plan_visible()
    {
        var cut = RenderComponent<Routine>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".gsm-m-ex__head")));
        cut.Find(".gsm-m-ex__head").Click();

        cut.Find($"[data-testid='other-{LineId}']").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll($"[data-testid='ex-{LineId}-pick-{SuggestedId}']")));
        cut.Find($"[data-testid='ex-{LineId}-pick-{SuggestedId}']").Click();

        Assert.Contains("Plancha lateral", cut.Find(".gsm-m-ex__name").TextContent);
        Assert.Contains("en lugar de Sentadilla con barra", cut.Find($"[data-testid='replaced-{LineId}']").TextContent);

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Guardar").Click();

        cut.WaitForAssertion(() =>
        {
            var post = Assert.Single(_api.Requests, r => r.Method == HttpMethod.Post && r.Path.Contains("/log"));
            using var doc = JsonDocument.Parse(post.Body);
            Assert.Equal(SuggestedId, doc.RootElement.GetProperty("replacementExerciseId").GetGuid());
        });
    }

    [Fact]
    public void Without_choosing_an_alternative_the_log_carries_no_replacement()
    {
        var cut = RenderComponent<Routine>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".gsm-m-ex__head")));
        cut.Find(".gsm-m-ex__head").Click();

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Guardar").Click();

        cut.WaitForAssertion(() =>
        {
            var post = Assert.Single(_api.Requests, r => r.Method == HttpMethod.Post && r.Path.Contains("/log"));
            using var doc = JsonDocument.Parse(post.Body);
            Assert.False(doc.RootElement.TryGetProperty("replacementExerciseId", out var value)
                         && value.ValueKind != JsonValueKind.Null);
        });
    }
}
