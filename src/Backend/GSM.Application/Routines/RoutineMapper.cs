using GSM.Application.Routines.Contracts;
using GSM.Domain.Routines;

namespace GSM.Application.Routines;

/// <summary>
/// Mapeo manual: la respuesta enriquece cada línea con el nombre del ejercicio (resuelto contra
/// el catálogo visible) y el nombre del día cae en <c>"Día {position}"</c> cuando falta.
/// </summary>
public static class RoutineMapper
{
    public static RoutineResponse ToResponse(Routine routine, IReadOnlyDictionary<Guid, string> exerciseNames)
        => new(
            routine.Id,
            routine.MemberId,
            routine.Name,
            routine.Notes,
            routine.AssignedBy,
            routine.SelfAssigned,
            routine.StartsOn,
            routine.IsActive,
            routine.DeactivatedOn,
            routine.CycleLength,
            routine.Days.OrderBy(d => d.Position).Select(d => ToDayResponse(d, exerciseNames)).ToList());

    public static RoutineDayResponse ToDayResponse(
        RoutineDay day, IReadOnlyDictionary<Guid, string> exerciseNames)
        => new(
            day.Id,
            day.Position,
            day.Name ?? $"Día {day.Position}",
            day.IsRest,
            day.Exercises.OrderBy(e => e.Order).Select(e => ToExerciseResponse(e, exerciseNames)).ToList());

    public static RoutineExerciseResponse ToExerciseResponse(
        RoutineExercise exercise, IReadOnlyDictionary<Guid, string> exerciseNames)
        => new(
            exercise.Id,
            exercise.ExerciseId,
            exerciseNames.GetValueOrDefault(exercise.ExerciseId, "?"),
            exercise.Order,
            exercise.TargetSets,
            exercise.TargetReps,
            exercise.TargetWeightKg,
            exercise.RestSeconds,
            exercise.Notes);

    public static RoutineListItem ToListItem(Routine routine)
        => new(routine.Id, routine.Name, routine.StartsOn, routine.IsActive, routine.DeactivatedOn, routine.CycleLength);

    public static IReadOnlyList<Guid> ExerciseIds(Routine routine)
        => routine.Days.SelectMany(d => d.Exercises.Select(e => e.ExerciseId)).Distinct().ToList();
}
