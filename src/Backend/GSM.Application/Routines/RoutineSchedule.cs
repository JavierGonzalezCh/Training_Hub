using GSM.Domain.Routines;

namespace GSM.Application.Routines;

/// <summary>
/// Qué día de la rutina toca en una fecha, según su modo de calendario. Función pura: la usan el lector de
/// calendario (racha) y la "rutina de hoy" del miembro, para que los dos digan siempre lo mismo.
/// </summary>
public static class RoutineSchedule
{
    /// <summary>
    /// El día vigente que toca en <paramref name="date"/>. En modo semanal <c>null</c> significa descanso (el día
    /// de la semana sin fila); en el rotativo siempre hay un día salvo que la rutina no tenga ninguno.
    /// </summary>
    public static RoutineDay? DayOn(Routine routine, DateOnly date)
    {
        var days = routine.Days;
        if (days.Count == 0)
        {
            return null;
        }

        if (routine.ScheduleMode == ScheduleMode.Weekly)
        {
            var weekday = IsoWeekDay.Of(date);
            return days.FirstOrDefault(d => d.DayOfWeek == weekday);
        }

        var position = RoutineCycle.PositionOn(routine.StartsOn, days.Count, date);
        return days.FirstOrDefault(d => d.Position == position);
    }

    /// <summary>
    /// Como <see cref="DayOn"/>, pero con el cambio que el miembro haya decidido para esa fecha (SP): devuelve el día que
    /// cuenta y, si cambió, el que tocaba según el plan. El cambio se ignora si no es de esta rutina, si el día que tocaba
    /// era descanso o si el día que se hace ya no está en la rutina (misma regla que <c>RoutineCalendar.Resolve</c>).
    /// </summary>
    public static async Task<(RoutineDay? Day, RoutineDay? Planned)> DayOnWithOverrideAsync(
        Routine routine, DateOnly date, Abstractions.IRoutineDayOverrideRepository overrides, Guid memberId,
        CancellationToken cancellationToken)
    {
        // Rotativa con el ciclo corrido: cada corrida hasta esta fecha atrasa el ciclo un día.
        var shifts = routine.ScheduleMode == ScheduleMode.Rotating
            ? await overrides.CountShiftsAsync(memberId, routine.Id, date, cancellationToken)
            : 0;
        var day = DayOn(routine, date.AddDays(-shifts));
        if (day is null || day.IsRest)
        {
            return (day, null);
        }

        var change = (await overrides.ListAsync(memberId, date, date, cancellationToken))
            .FirstOrDefault(o => o.RoutineId == routine.Id);
        if (change?.Reason == RoutineDayOverrideReason.Shift)
        {
            // El día en que se corrió: cuenta el corrido «en lugar de» el que tocaba antes de correr.
            var before = DayOn(routine, date.AddDays(-(shifts - 1)));
            return before is null || before.Id == day.Id ? (day, null) : (day, before);
        }

        var performed = change is null ? null
            : routine.Days.FirstOrDefault(d => d.Id == change.PerformedDayId && !d.IsRest);
        return performed is null || performed.Id == day.Id ? (day, null) : (performed, day);
    }

    public static string ModeToText(ScheduleMode mode)
        => mode == ScheduleMode.Weekly ? "weekly" : "rotating";

    /// <summary>Nulo o vacío = rotativo (compatibilidad con los clientes anteriores al modo semanal).</summary>
    public static ScheduleMode ParseMode(string? text)
        => string.IsNullOrWhiteSpace(text) ? ScheduleMode.Rotating
            : text.Trim().ToLowerInvariant() switch
            {
                "rotating" => ScheduleMode.Rotating,
                "weekly" => ScheduleMode.Weekly,
                _ => throw new Domain.Common.Exceptions.ValidationDomainException(
                    "El modo de calendario debe ser \"weekly\" o \"rotating\"."),
            };
}
