using GSM.Client.Generated;
using GSM.UI.Shared.Routines;
using Xunit;

namespace GSM.Web.Tests;

/// <summary>R4: textos y comparación planeado vs hecho de las series.</summary>
public sealed class SetPlanTests
{
    private static SetTargetResponse Target(string type = "normal", int? min = 8, int? max = 12, double? kg = 40, int? seconds = null, int? meters = null)
        => new() { Number = 1, SetType = type, RepsMin = min, RepsMax = max, WeightKg = kg, DurationSeconds = seconds, DistanceM = meters };

    [Fact]
    public void Describes_a_target_with_range_weight_and_rir_and_marks_unilateral_per_side()
    {
        var target = Target() ;
        target.Rir = 2;

        Assert.Equal("8–12 · 40 kg · RIR 2", SetPlan.Describe(target));
        Assert.Equal("8–12 por lado · 40 kg · RIR 2", SetPlan.Describe(target, unilateral: true));
        Assert.Equal("al fallo · 40 kg", SetPlan.Describe(Target("failure", null, null)));
        Assert.Equal("45 s", SetPlan.Describe(Target(min: null, max: null, kg: null, seconds: 45)));
        Assert.Equal("1.5 km", SetPlan.Describe(Target(min: null, max: null, kg: null, meters: 1500)));
    }

    [Fact]
    public void Summary_groups_consecutive_identical_series()
    {
        var targets = new[]
        {
            new SetTargetResponse { Number = 1, SetType = "warmup", RepsMin = 12, RepsMax = 12, WeightKg = 20 },
            new SetTargetResponse { Number = 2, SetType = "normal", RepsMin = 8, RepsMax = 12, WeightKg = 40 },
            new SetTargetResponse { Number = 3, SetType = "normal", RepsMin = 8, RepsMax = 12, WeightKg = 40 },
        };

        Assert.Equal("Calentamiento 1 × 12 · 20 kg; 2 × 8–12 · 40 kg", SetPlan.Summary(targets));
    }

    [Theory]
    [InlineData(7, 40.0, SetOutcome.Below)]
    [InlineData(8, 40.0, SetOutcome.Met)]
    [InlineData(12, 40.0, SetOutcome.Met)]
    [InlineData(13, 40.0, SetOutcome.Exceeded)]
    [InlineData(10, 35.0, SetOutcome.Below)]   // lo peor gana: menos peso
    [InlineData(10, 45.0, SetOutcome.Exceeded)]
    public void Compares_a_done_set_against_its_target(int reps, double kg, SetOutcome expected)
        => Assert.Equal(expected, SetPlan.Compare(Target(), new DoneSet(reps, kg, null, null)));

    [Fact]
    public void Failure_sets_are_met_with_at_least_one_rep_and_extras_and_missing_are_flagged()
    {
        Assert.Equal(SetOutcome.Met, SetPlan.Compare(Target("failure", null, null, null), new DoneSet(6, null, null, null)));
        Assert.Equal(SetOutcome.Below, SetPlan.Compare(Target("failure", null, null, null), new DoneSet(0, null, null, null)));
        Assert.Equal(SetOutcome.Extra, SetPlan.Compare(null, new DoneSet(10, 40, null, null)));
        Assert.Equal(SetOutcome.NotDone, SetPlan.Compare(Target(), null));

        var outcomes = SetPlan.CompareAll(
            [Target(), Target()],
            [new DoneSet(10, 40, null, null), new DoneSet(10, 40, null, null), new DoneSet(10, 40, null, null)]);
        Assert.Equal([SetOutcome.Met, SetOutcome.Met, SetOutcome.Extra], outcomes);
        Assert.Equal("2 de 2 series cumplidas", SetPlan.ProgressText(outcomes));
    }

    [Fact]
    public void Time_and_distance_targets_are_compared_by_value()
    {
        var run = Target(min: null, max: null, kg: null, seconds: 600, meters: 2000);
        Assert.Equal(SetOutcome.Met, SetPlan.Compare(run, new DoneSet(null, null, 600, 2000)));
        Assert.Equal(SetOutcome.Below, SetPlan.Compare(run, new DoneSet(null, null, 600, 1500)));
        Assert.Equal(SetOutcome.Exceeded, SetPlan.Compare(run, new DoneSet(null, null, 650, 2000)));
    }

    [Fact]
    public void Group_labels_letter_the_supersets_and_skip_loose_lines()
    {
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        var lines = new[]
        {
            new RoutineExerciseResponse { Id = ids[0], Group = 3 },
            new RoutineExerciseResponse { Id = ids[1], Group = 3 },
            new RoutineExerciseResponse { Id = ids[2] },
            new RoutineExerciseResponse { Id = ids[3], Group = 8 },
            new RoutineExerciseResponse { Id = ids[4], Group = 8 },
        };

        var labels = SetPlan.GroupLabels(lines);

        Assert.Equal(["A1", "A2", "B1", "B2"], new[] { labels[ids[0]], labels[ids[1]], labels[ids[3]], labels[ids[4]] });
        Assert.DoesNotContain(ids[2], labels.Keys);
    }
}
