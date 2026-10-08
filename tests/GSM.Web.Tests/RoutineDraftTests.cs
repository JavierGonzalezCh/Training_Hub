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
                    Exercises = [new DraftExercise { Exercise = exercise, TargetWeightKg = 42.5m }, new DraftExercise()],
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
        Assert.Equal(42.5, only.TargetWeightKg);
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
                            Id = lineId, ExerciseId = exerciseId, ExerciseName = "Press", TargetSets = 3, TargetReps = "10",
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
}
