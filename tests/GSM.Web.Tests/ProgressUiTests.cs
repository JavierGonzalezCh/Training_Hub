using Bunit;
using GSM.Client.Generated;
using GSM.UI.Shared.Routines;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Xunit;

namespace GSM.Web.Tests;

/// <summary>R7: progreso por ejercicio y récords personales.</summary>
public class ProgressUiTests : TestContext
{
    private static readonly Guid BenchId = Guid.Parse("00000000-0000-0000-0000-0000000000b1");

    public ProgressUiTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static ExerciseProgressSummary Summary() => new()
    {
        ExerciseId = BenchId,
        ExerciseName = "Press de banca",
        TrackingType = "RepsWeight",
        IsUnilateral = false,
        Sessions = 2,
        LastPerformedOn = new DateOnly(2026, 6, 8),
        Records = [new PersonalRecordItem { Kind = "best_weight", Value = 85, Unit = "kg", AchievedOn = new DateOnly(2026, 6, 8) }],
    };

    private static ExerciseProgressResponse Detail() => new()
    {
        ExerciseId = BenchId,
        ExerciseName = "Press de banca",
        TrackingType = "RepsWeight",
        IsUnilateral = false,
        Sessions = 2,
        Points =
        [
            new ProgressPoint { PerformedOn = new DateOnly(2026, 6, 1), SetCount = 3, BestWeightKg = 80, BestSetVolume = 400, TotalVolume = 1200 },
            new ProgressPoint { PerformedOn = new DateOnly(2026, 6, 8), SetCount = 3, BestWeightKg = 85, BestSetVolume = 425, TotalVolume = 1275 },
        ],
        Records =
        [
            new PersonalRecordItem { Kind = "best_weight", Value = 85, Unit = "kg", AchievedOn = new DateOnly(2026, 6, 8) },
            new PersonalRecordItem { Kind = "best_session_volume", Value = 1275, Unit = "kg", AchievedOn = new DateOnly(2026, 6, 8) },
        ],
    };

    [Fact]
    public void Lists_the_logged_exercises_with_their_best_mark()
    {
        var cut = RenderComponent<ProgressPanel>(p => p
            .Add(x => x.LoadSummaries, () => Task.FromResult<ICollection<ExerciseProgressSummary>>([Summary()]))
            .Add(x => x.LoadDetail, _ => Task.FromResult(Detail())));

        var head = cut.Find($"[data-testid='progress-{BenchId}']").TextContent;
        Assert.Contains("Press de banca", head);
        Assert.Contains("2 sesiones", head);
        Assert.Contains("Mejor peso: 85 kg", head);
    }

    [Fact]
    public void Opening_an_exercise_shows_its_records_and_the_evolution_chart()
    {
        var cut = RenderComponent<ProgressPanel>(p => p
            .Add(x => x.LoadSummaries, () => Task.FromResult<ICollection<ExerciseProgressSummary>>([Summary()]))
            .Add(x => x.LoadDetail, _ => Task.FromResult(Detail())));

        cut.Find(".mud-expand-panel-header").Click();

        cut.WaitForAssertion(() =>
        {
            var records = cut.Find($"[data-testid='records-{BenchId}']").TextContent;
            Assert.Contains("Mejor volumen de un día", records);
            Assert.Contains("1275 kg", records);
            Assert.NotNull(cut.Find($"[data-testid='chart-{BenchId}']"));
        });
    }

    [Fact]
    public void Shows_the_empty_text_when_nothing_was_logged()
    {
        var cut = RenderComponent<ProgressPanel>(p => p
            .Add(x => x.LoadSummaries, () => Task.FromResult<ICollection<ExerciseProgressSummary>>([]))
            .Add(x => x.LoadDetail, _ => Task.FromResult(Detail()))
            .Add(x => x.EmptyText, "Aún sin registros."));

        Assert.Contains("Aún sin registros.", cut.Find("[data-testid='progress-empty']").TextContent);
    }

    [Fact]
    public void Substitutions_list_shows_the_date_the_planned_and_the_performed_exercise()
    {
        var planned = Guid.NewGuid();
        var cut = RenderComponent<SubstitutionsList>(p => p.Add(x => x.Load, () => Task.FromResult<ICollection<ExerciseSubstitutionItem>>(
        [
            new ExerciseSubstitutionItem
            {
                PerformedOn = new DateOnly(2026, 6, 8), PlannedExerciseId = planned, PlannedExerciseName = "Press de banca",
                PerformedExerciseId = Guid.NewGuid(), PerformedExerciseName = "Press con mancuernas",
            },
        ])));

        var row = cut.Find($"[data-testid='substitution-2026-06-08-{planned}']").TextContent;
        Assert.Contains("Press con mancuernas", row);
        Assert.Contains("en lugar de Press de banca", row);
    }

    [Fact]
    public void Substitutions_list_says_so_when_there_are_none()
    {
        var cut = RenderComponent<SubstitutionsList>(p => p.Add(x => x.Load, () => Task.FromResult<ICollection<ExerciseSubstitutionItem>>([])));

        Assert.Contains("Sin sustituciones", cut.Find("[data-testid='substitutions-empty']").TextContent);
    }

    [Fact]
    public void Labels_format_values_by_unit_and_pick_the_measure_the_exercise_tracks()
    {
        Assert.Equal("85 kg", ProgressLabels.FormatValue(85, "kg"));
        Assert.Equal("400 m", ProgressLabels.FormatValue(400, "m"));
        Assert.Equal("1:30", ProgressLabels.FormatValue(90, "s"));
        Assert.Equal("Mayor tiempo", ProgressLabels.RecordLabel("best_duration"));

        var point = new ProgressPoint { PerformedOn = new DateOnly(2026, 6, 1), BestWeightKg = 50, BestDurationSeconds = 45, TotalReps = 30 };
        Assert.Equal(50, ProgressLabels.Primary("RepsWeight").Pick(point));
        Assert.Equal(45, ProgressLabels.Primary("Time").Pick(point));
        Assert.Equal(30, ProgressLabels.Primary("Reps").Pick(point));
    }

    [Fact]
    public void The_chart_line_spans_the_series_and_handles_a_flat_one()
    {
        var series = new List<(DateOnly Day, double Value)>
        {
            (new DateOnly(2026, 6, 1), 80), (new DateOnly(2026, 6, 8), 85),
        };
        var points = ProgressLabels.PolylinePoints(series).Split(' ');

        Assert.Equal(2, points.Length);
        Assert.StartsWith("6,", points[0]);
        Assert.StartsWith("294,", points[1]);
        Assert.Equal(2, ProgressLabels.PolylinePoints([(new DateOnly(2026, 6, 1), 80), (new DateOnly(2026, 6, 8), 80)]).Split(' ').Length);
    }
}
