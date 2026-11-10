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
        Services.AddSingleton<GSM.Client.Http.SessionEvents>();
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

    // --- Editor: las alternativas no se eligen al armar la rutina ---

    [Fact]
    public void The_routine_form_has_no_change_for_the_system_suggests_alternatives_while_training()
    {
        RenderComponent<MudPopoverProvider>();
        var cut = RenderComponent<RoutineForm>(p => p.Add(x => x.Draft, DraftWithExistingLine()));

        Assert.Empty(cut.FindAll("[data-testid^='change-']"));
        Assert.DoesNotContain("Cambiar por", cut.Markup);
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

        cut.Find(".gsm-m-setrow input").Input("5");
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Guardar").Click();

        cut.WaitForAssertion(() =>
        {
            var post = Assert.Single(_api.Requests, r => r.Method == HttpMethod.Post && r.Path.Contains("/log"));
            using var doc = JsonDocument.Parse(post.Body);
            Assert.Equal(SuggestedId, doc.RootElement.GetProperty("replacementExerciseId").GetGuid());
        });
    }

    [Fact]
    public void Set_fields_start_empty_and_saving_without_anything_typed_sends_nothing()
    {
        var cut = RenderComponent<Routine>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".gsm-m-ex__head")));
        cut.Find(".gsm-m-ex__head").Click();

        Assert.All(cut.FindAll(".gsm-m-setrow input"), i => Assert.True(string.IsNullOrEmpty(i.GetAttribute("value"))));

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Guardar").Click();

        Assert.DoesNotContain(_api.Requests, r => r.Method == HttpMethod.Post && r.Path.Contains("/log"));
    }

    [Fact]
    public void Without_choosing_an_alternative_the_log_carries_no_replacement()
    {
        var cut = RenderComponent<Routine>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".gsm-m-ex__head")));
        cut.Find(".gsm-m-ex__head").Click();

        cut.Find(".gsm-m-setrow input").Input("5");
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
