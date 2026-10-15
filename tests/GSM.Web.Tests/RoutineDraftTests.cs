using GSM.Client.Generated;
using GSM.UI.Shared.Routines;
using Xunit;

namespace GSM.Web.Tests;

public sealed class RoutineDraftTests
{
    [Fact]
    public void Empty_draft_starts_with_one_day_and_requires_a_name()
    {
        var draft = RoutineDraft.Empty();

        Assert.Single(draft.Days);
        Assert.NotNull(draft.Validate());

        draft.Name = "Full body";
        Assert.Null(draft.Validate());
    }

    [Fact]
    public void ToRequest_drops_rest_day_exercises_and_rows_without_exercise()
    {
        var exercise = new ExerciseResponse { Id = Guid.NewGuid(), Name = "Sentadilla" };
        var draft = new RoutineDraft
        {
            Name = "Pierna",
            Days =
            [
                new DraftDay
                {
                    Name = "  ",
                    Exercises = [new DraftExercise { Exercise = exercise, Sets = [new DraftSet { RepsMin = 8, RepsMax = 10, WeightKg = 42.5m }] }, new DraftExercise()],
                },
                new DraftDay { IsRest = true, Exercises = [new DraftExercise { Exercise = exercise }] },
            ],
        };

        var request = draft.ToRequest();

        Assert.Equal(2, request.Days.Count);
        var first = request.Days.First();
        Assert.Null(first.Name);
        var only = Assert.Single(first.Exercises);
        Assert.Equal(exercise.Id, only.ExerciseId);
        var set = Assert.Single(only.SetTargets);
        Assert.Equal((8, 10, 42.5), (set.RepsMin, set.RepsMax, set.WeightKg));
        Assert.Empty(request.Days.Last().Exercises);
    }

    [Fact]
    public void Editing_keeps_day_and_line_ids_and_new_rows_go_without_id()
    {
        var dayId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var exerciseId = Guid.NewGuid();
        var routine = new RoutineResponse
        {
            Name = "Rutina",
            Days =
            [
                new RoutineDayResponse
                {
                    Id = dayId,
                    Name = "D1",
                    Exercises =
                    [
                        new RoutineExerciseResponse
                        {
                            Id = lineId, ExerciseId = exerciseId, ExerciseName = "Press", Group = 2,
                            SetTargets = [new SetTargetResponse { Number = 1, SetType = "warmup", RepsMin = 12, RepsMax = 12 }, new SetTargetResponse { Number = 2, SetType = "normal", RepsMin = 8, RepsMax = 10, WeightKg = 40 }],
                        },
                    ],
                },
            ],
        };

        var draft = RoutineDraft.From(routine);
        draft.Days[0].Exercises.Add(new DraftExercise { Exercise = new ExerciseResponse { Id = Guid.NewGuid(), Name = "Remo" } });
        draft.Days.Add(new DraftDay { Name = "Nuevo" });

        var request = draft.ToRequest();

        Assert.Equal(dayId, request.Days.First().Id);
        Assert.Equal(lineId, request.Days.First().Exercises.First().Id);
        Assert.Null(request.Days.First().Exercises.Last().Id);
        Assert.Null(request.Days.Last().Id);
    }

    [Fact]
    public void Editing_loads_the_series_one_by_one_and_sends_them_back()
    {
        var routine = new RoutineResponse
        {
            Name = "Rutina",
            Days =
            [
                new RoutineDayResponse
                {
                    Id = Guid.NewGuid(), Name = "D1",
                    Exercises =
                    [
                        new RoutineExerciseResponse
                        {
                            Id = Guid.NewGuid(), ExerciseId = Guid.NewGuid(), ExerciseName = "Press",
                            SetTargets = [new SetTargetResponse { Number = 1, SetType = "warmup", RepsMin = 12, RepsMax = 12 }, new SetTargetResponse { Number = 2, SetType = "normal", RepsMin = 8, RepsMax = 10, WeightKg = 40, Rir = 2 }],
                        },
                    ],
                },
            ],
        };

        var draft = RoutineDraft.From(routine);
        Assert.Equal(2, draft.Days[0].Exercises[0].Sets.Count);

        var sent = draft.ToRequest().Days.Single().Exercises.Single().SetTargets.ToList();
        Assert.Equal(["warmup", "normal"], sent.Select(s => s.SetType));
        Assert.Equal((8, 10, 40.0, 2), (sent[1].RepsMin, sent[1].RepsMax, sent[1].WeightKg, sent[1].Rir));
    }

    [Fact]
    public void Superset_groups_are_renumbered_split_when_not_contiguous_and_dropped_when_alone()
    {
        var ex = new ExerciseResponse { Id = Guid.NewGuid(), Name = "X" };
        DraftExercise Line(int? group) => new() { Exercise = ex, Group = group };
        var draft = new RoutineDraft
        {
            Name = "R",
            Days = [new DraftDay { Exercises = [Line(5), Line(5), Line(null), Line(5), Line(9), Line(9), Line(9)] }],
        };

        var groups = draft.ToRequest().Days.Single().Exercises.Select(e => e.Group).ToList();

        // 5,5 forman el grupo 1; el 5 suelto del medio se queda solo (null); 9,9,9 es el grupo 2.
        Assert.Equal([1, 1, null, null, 2, 2, 2], groups);
    }

    [Fact]
    public void Linking_with_the_previous_line_forms_a_superset_and_unlinking_splits_the_tail()
    {
        var ex = new ExerciseResponse { Id = Guid.NewGuid(), Name = "X" };
        var day = new DraftDay { Exercises = [new DraftExercise { Exercise = ex }, new DraftExercise { Exercise = ex }, new DraftExercise { Exercise = ex }] };

        day.ToggleLinkWithPrevious(day.Exercises[1]);
        day.ToggleLinkWithPrevious(day.Exercises[2]);
        Assert.True(day.IsLinkedWithPrevious(day.Exercises[1]));
        Assert.True(day.IsLinkedWithPrevious(day.Exercises[2]));

        day.ToggleLinkWithPrevious(day.Exercises[1]);
        Assert.False(day.IsLinkedWithPrevious(day.Exercises[1]));
        Assert.True(day.IsLinkedWithPrevious(day.Exercises[2]));   // la cola viaja con la línea separada
        Assert.NotEqual(day.Exercises[0].Group, day.Exercises[1].Group);
    }

    [Fact]
    public void Choosing_an_exercise_that_measures_time_replaces_the_default_series()
    {
        var line = new DraftExercise();
        Assert.Equal(3, line.Sets.Count);

        line.Select(new ExerciseResponse { Id = Guid.NewGuid(), Name = "Plancha", TrackingType = "Time" });

        var set = Assert.Single(line.Sets);
        Assert.Null(set.RepsMin);
        Assert.Equal(30, set.DurationSeconds);
        Assert.False(line.Fields.Reps);
        Assert.True(line.Fields.Time);
    }
}
