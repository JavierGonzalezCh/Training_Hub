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
    /// <param name="PlannedDay">Con un ajuste del miembro (SP), el día que tocaba según el plan; si no, <c>null</c>.</param>
    public sealed record Resolution(
        AdherenceRoutine? Routine, AdherenceDay? Day, bool IsPlannedRest, AdherenceDay? PlannedDay = null)
    {
        /// <summary>Toca entrenar.</summary>
        public bool IsTraining => Day is { IsRest: false };

        /// <summary>El miembro cambió lo que tocaba en esta fecha (sesión recuperada o semana reorganizada).</summary>
        public bool IsOverridden => PlannedDay is not null;
    }

    /// <summary>Más reciente primero y, a igual inicio, la activa primero (la rutina vigente es la primera que cubre la fecha).</summary>
    public static List<AdherenceRoutine> Order(IEnumerable<AdherenceRoutine> routines)
        => routines.OrderByDescending(r => r.StartsOn).ThenByDescending(r => r.IsActive).ToList();

    /// <param name="ordered">Rutinas ya ordenadas con <see cref="Order"/>.</param>
    /// <param name="overrides">
    /// Ajustes del miembro por fecha (SP): el día que cuenta en lugar del planeado. Solo aplican si el ajuste es de la
    /// rutina vigente, el día que tocaba era de entrenamiento y el día que se hace está vigente esa fecha; si no, se ignoran.
    /// </param>
    public static Resolution Resolve(
        IReadOnlyList<AdherenceRoutine> ordered, DateOnly date,
        IReadOnlyDictionary<DateOnly, AdherenceOverride>? overrides = null)
    {
        var planned = ResolvePlanned(ordered, date, 0);

        // Rotativa con el ciclo corrido (SP): cada corrida hasta esta fecha atrasa el ciclo un día.
        var shifts = planned.Routine is { Mode: ScheduleMode.Rotating } routine && overrides is not null
            ? ShiftsUpTo(overrides, routine.Id, date)
            : 0;
        if (shifts > 0)
        {
            var shifted = ResolvePlanned(ordered, date, shifts);
            if (overrides!.TryGetValue(date, out var today) && today.Reason == RoutineDayOverrideReason.Shift
                && shifted.IsTraining)
            {
                // El día en que se corrió: cuenta el día corrido «en lugar de» el que tocaba antes de correr.
                var before = ResolvePlanned(ordered, date, shifts - 1);
                return before.Day is { } beforeDay && beforeDay.Id != shifted.Day!.Id
                    ? shifted with { PlannedDay = beforeDay }
                    : shifted;
            }

            planned = shifted;
        }

        if (overrides is null || !overrides.TryGetValue(date, out var change)
            || change.Reason == RoutineDayOverrideReason.Shift
            || planned.Routine is null || planned.Routine.Id != change.RoutineId || !planned.IsTraining)
        {
            return planned;
        }

        var performed = planned.Routine.Days.FirstOrDefault(d =>
            d.Id == change.PerformedDayId && !d.IsRest && InForce(d, date));
        return performed is null || performed.Id == planned.Day!.Id
            ? planned
            : new Resolution(planned.Routine, performed, false, planned.Day);
    }

    /// <summary>Cuántas veces se corrió el ciclo de una rutina rotativa hasta <paramref name="date"/> (incluida).</summary>
    public static int ShiftsUpTo(IReadOnlyDictionary<DateOnly, AdherenceOverride> overrides, Guid routineId, DateOnly date)
        => overrides.Values.Count(o =>
            o.Reason == RoutineDayOverrideReason.Shift && o.RoutineId == routineId && o.Date <= date);

    /// <summary>Ajustes de un miembro indexados por fecha, para <see cref="Resolve"/>.</summary>
    public static IReadOnlyDictionary<DateOnly, AdherenceOverride> ByDate(IEnumerable<AdherenceOverride> overrides)
        => overrides.GroupBy(o => o.Date).ToDictionary(g => g.Key, g => g.First());

    /// <summary>¿El día estaba vigente en esa fecha?</summary>
    public static bool InForce(AdherenceDay day, DateOnly date)
        => day.AddedOn <= date && (day.RetiredOn is null || date < day.RetiredOn);

    /// <summary>Lo que toca según el plan, sin ajustes del miembro.</summary>
    /// <param name="shifts">Días que se corrió el ciclo (solo rotativas).</param>
    private static Resolution ResolvePlanned(IReadOnlyList<AdherenceRoutine> ordered, DateOnly date, int shifts)
    {
        var routine = ordered.FirstOrDefault(r =>
            r.StartsOn <= date && (r.DeactivatedOn is null || date <= r.DeactivatedOn));
        if (routine is null)
        {
            return new Resolution(null, null, false);
        }

        var inForce = routine.Days
            .Where(d => InForce(d, date))
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
        var current = cycle[RoutineCycle.PositionOn(routine.StartsOn, cycle.Count, date.AddDays(-shifts)) - 1];
        return new Resolution(routine, current, current.IsRest);
    }
}
