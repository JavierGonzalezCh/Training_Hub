using GSM.Application.Routines.Abstractions;
using GSM.Domain.Routines;

namespace GSM.Application.Routines;

/// <summary>
/// Qué toca en una fecha cuando se juzga con la rutina y los días <b>vigentes ese día</b> (R2b). Es la única copia de esta
/// regla: la usan el calendario de la racha (<c>RoutineReader</c>) y el cumplimiento de la rutina
/// (<c>AdherenceCalculator</c>), para que los dos digan siempre lo mismo de cada fecha. Función pura.
/// </summary>
public static class RoutineCalendar
{
    /// <param name="Routine">La rutina vigente ese día; <c>null</c> si no había ninguna.</param>
    /// <param name="Day">El día del ciclo o de la semana que toca; <c>null</c> si no hay (descanso sin fila, o sin días vigentes).</param>
    /// <param name="IsPlannedRest">Descanso planificado: un día marcado como descanso, o en modo semanal un día de la semana sin fila.</param>
    public sealed record Resolution(AdherenceRoutine? Routine, AdherenceDay? Day, bool IsPlannedRest)
    {
        /// <summary>Toca entrenar.</summary>
        public bool IsTraining => Day is { IsRest: false };
    }

    /// <summary>Más reciente primero y, a igual inicio, la activa primero (la rutina vigente es la primera que cubre la fecha).</summary>
    public static List<AdherenceRoutine> Order(IEnumerable<AdherenceRoutine> routines)
        => routines.OrderByDescending(r => r.StartsOn).ThenByDescending(r => r.IsActive).ToList();

    /// <param name="ordered">Rutinas ya ordenadas con <see cref="Order"/>.</param>
    public static Resolution Resolve(IReadOnlyList<AdherenceRoutine> ordered, DateOnly date)
    {
        var routine = ordered.FirstOrDefault(r =>
            r.StartsOn <= date && (r.DeactivatedOn is null || date <= r.DeactivatedOn));
        if (routine is null)
        {
            return new Resolution(null, null, false);
        }

        var inForce = routine.Days
            .Where(d => d.AddedOn <= date && (d.RetiredOn is null || date < d.RetiredOn))
            .ToList();
        if (inForce.Count == 0)
        {
            return new Resolution(routine, null, false);
        }

        if (routine.Mode == ScheduleMode.Weekly)
        {
            // Semanal: el día de la semana; sin fila vigente, descanso planificado.
            var weekday = IsoWeekDay.Of(date);
            var day = inForce.FirstOrDefault(d => d.DayOfWeek == weekday);
            return new Resolution(routine, day, day is null || day.IsRest);
        }

        // Rotativo: el ciclo de ese día tiene los días vigentes ese día, ordenados por posición.
        var cycle = inForce.OrderBy(d => d.Position).ThenBy(d => d.AddedOn).ToList();
        var current = cycle[RoutineCycle.PositionOn(routine.StartsOn, cycle.Count, date) - 1];
        return new Resolution(routine, current, current.IsRest);
    }
}
