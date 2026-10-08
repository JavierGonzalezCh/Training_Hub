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
