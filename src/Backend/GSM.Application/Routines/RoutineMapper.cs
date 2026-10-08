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
            routine.Days.OrderBy(d => d.Position).Select(d => ToDayResponse(d, exerciseNames)).ToList(),
            RoutineSchedule.ModeToText(routine.ScheduleMode));

    public static RoutineDayResponse ToDayResponse(
        RoutineDay day, IReadOnlyDictionary<Guid, string> exerciseNames)
        => new(
            day.Id,
            day.Position,
            DayName(day),
            day.IsRest,
            day.Exercises.OrderBy(e => e.Order).Select(e => ToExerciseResponse(e, exerciseNames)).ToList(),
            day.DayOfWeek);

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

    private static readonly string[] WeekDayNames =
        ["Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado", "Domingo"];

    /// <summary>Nombre del día de la semana ISO (1 = lunes) en español.</summary>
    public static string WeekDayName(int isoDay) => WeekDayNames[isoDay - 1];

    /// <summary>El nombre del día; si no lo tiene, el de la semana (modo semanal) o "Día N" (rotativo).</summary>
    public static string DayName(RoutineDay day)
        => day.Name ?? (day.DayOfWeek is { } weekday ? WeekDayName(weekday) : $"Día {day.Position}");

    public static RoutineListItem ToListItem(Routine routine)
        => new(routine.Id, routine.Name, routine.StartsOn, routine.IsActive, routine.DeactivatedOn, routine.CycleLength,
            RoutineSchedule.ModeToText(routine.ScheduleMode));

    public static IReadOnlyList<Guid> ExerciseIds(Routine routine)
        => routine.Days.SelectMany(d => d.Exercises.Select(e => e.ExerciseId)).Distinct().ToList();
}
