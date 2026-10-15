using GSM.Application.Routines.Abstractions;
using GSM.Application.Routines.Contracts;
using GSM.Domain.Routines;

namespace GSM.Application.Routines;

/// <summary>
/// Mapeo manual: la respuesta enriquece cada línea con el nombre del ejercicio (resuelto contra
/// el catálogo visible) y el nombre del día cae en <c>"Día {position}"</c> cuando falta.
/// </summary>
public static class RoutineMapper
{
    public static RoutineResponse ToResponse(
        Routine routine,
        IReadOnlyDictionary<Guid, ExerciseSummary> exercises,
        IReadOnlyDictionary<Guid, ExerciseEquipmentInfo>? equipment = null)
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
            routine.Days.OrderBy(d => d.Position).Select(d => ToDayResponse(d, exercises, equipment)).ToList(),
            RoutineSchedule.ModeToText(routine.ScheduleMode));

    public static RoutineDayResponse ToDayResponse(
        RoutineDay day,
        IReadOnlyDictionary<Guid, ExerciseSummary> exercises,
        IReadOnlyDictionary<Guid, ExerciseEquipmentInfo>? equipment = null)
        => new(
            day.Id,
            day.Position,
            DayName(day),
            day.IsRest,
            day.Exercises.OrderBy(e => e.Order).Select(e => ToExerciseResponse(e, exercises, equipment)).ToList(),
            day.DayOfWeek,
            MuscleGroupsOf(day, exercises));

    /// <summary>Grupos musculares que trabaja el día, sin repetir y en orden de aparición.</summary>
    public static IReadOnlyList<string> MuscleGroupsOf(
        RoutineDay day, IReadOnlyDictionary<Guid, ExerciseSummary> exercises)
        => day.Exercises.OrderBy(e => e.Order)
            .Select(e => exercises.TryGetValue(e.ExerciseId, out var x) ? (MuscleGroup?)MuscleGroups.Of(x.PrimaryMuscle) : null)
            .Where(g => g is not null)
            .Select(g => g!.Value.ToString())
            .Distinct()
            .ToList();

    public static RoutineExerciseResponse ToExerciseResponse(
        RoutineExercise exercise,
        IReadOnlyDictionary<Guid, ExerciseSummary> exercises,
        IReadOnlyDictionary<Guid, ExerciseEquipmentInfo>? equipment = null)
    {
        exercises.TryGetValue(exercise.ExerciseId, out var info);
        return new(
            exercise.Id,
            exercise.ExerciseId,
            info?.Name ?? "?",
            exercise.Order,
            exercise.SetTargets.OrderBy(t => t.Number).Select(ToSetTargetResponse).ToList(),
            exercise.Group,
            exercise.RestSeconds,
            exercise.Notes,
            (info?.TrackingType ?? TrackingType.RepsWeight).ToString(),
            info?.IsUnilateral ?? false,
            (info is null ? MuscleGroup.FullBody : MuscleGroups.Of(info.PrimaryMuscle)).ToString(),
            equipment is not null && equipment.TryGetValue(exercise.ExerciseId, out var machines) ? machines : null);
    }

    public static SetTargetResponse ToSetTargetResponse(SetTarget t)
        => new(t.Number, SetTypes.ToText(t.SetType), t.RepsMin, t.RepsMax, t.WeightKg, t.DurationSeconds, t.DistanceM, t.Rir);

    public static SetResponse ToSetResponse(RoutineSetEntry s)
        => new(s.Number, SetTypes.ToText(s.SetType), s.Reps, s.WeightKg, s.DurationSeconds, s.DistanceM, s.Rir);

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

    /// <summary>Lo que Equipment necesita saber de cada ejercicio de la rutina (los tipos de equipo que pide).</summary>
    public static IReadOnlyList<ExerciseNeed> EquipmentNeeds(
        Routine routine, IReadOnlyDictionary<Guid, ExerciseSummary> exercises)
        => ExerciseIds(routine)
            .Select(id => new ExerciseNeed(
                id, exercises.TryGetValue(id, out var x) && x.Equipment is { } types ? types : []))
            .ToList();

    public static IReadOnlyList<Guid> ExerciseIds(Routine routine)
        => routine.Days.SelectMany(d => d.Exercises.Select(e => e.ExerciseId)).Distinct().ToList();
}
