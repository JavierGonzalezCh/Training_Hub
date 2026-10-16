using GSM.Application.Common.Abstractions;
using GSM.Application.Routines.Abstractions;
using GSM.Application.Routines.Public;

namespace GSM.Application.Routines;

/// <summary>
/// "Tu entrenamiento de hoy" para el inicio del miembro (D2). Resuelve el día con <see cref="RoutineSchedule"/>, la
/// misma función que usa <see cref="GetMyRoutineHandler"/>, para que el inicio y la pantalla de rutina digan lo mismo.
/// </summary>
public sealed class RoutineTodayReader(
    IClock clock,
    IRoutineRepository routines,
    IExerciseRepository exercises) : IRoutineTodayReader
{
    public async Task<TodayTrainingView?> GetAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        var routine = await routines.GetActiveForMemberWithDaysAsync(memberId, cancellationToken);
        if (routine is null)
        {
            return null;
        }

        var today = clock.Today;
        var day = RoutineSchedule.DayOn(routine, today);

        // Semanal sin fila para hoy = descanso: no hay día al que apuntar.
        var isRest = day is null || day.IsRest;
        var names = isRest
            ? new Dictionary<Guid, ExerciseSummary>()
            : await exercises.GetVisibleSummariesAsync(RoutineMapper.ExerciseIds(routine), cancellationToken);

        return new TodayTrainingView(
            routine.Id,
            routine.Name,
            today,
            day is null ? "Descanso" : RoutineMapper.DayName(day),
            isRest,
            isRest ? 0 : day!.Exercises.Count,
            isRest ? Array.Empty<string>() : RoutineMapper.MuscleGroupsOf(day!, names),
            routine.SelfAssigned ? null : routine.ReviewOn);
    }
}
