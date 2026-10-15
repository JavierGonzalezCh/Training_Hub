using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Memberships;

/// <summary>
/// Aritmética de fechas de las membresías por tiempo (Memberships v2, §3). Pura: sin reloj ni base.
/// Un plan de N meses que empieza el día D cubre hasta el día anterior a D, N meses después; si el
/// día no existe en el mes de destino se usa el último día de ese mes. Para no ir perdiendo días mes
/// a mes en una cadena de renovaciones, la membresía guarda el día del ciclo (<c>CycleDay</c>).
/// </summary>
public static class MembershipCalendar
{
    /// <summary>Último día cubierto (inclusive) de una membresía que empieza en <paramref name="start"/>.</summary>
    public static DateOnly ComputeEnd(DateOnly start, DurationUnit unit, int value, int? cycleDay)
    {
        if (value < 1)
        {
            throw new ValidationDomainException("La duración debe ser de al menos 1.");
        }

        return unit == DurationUnit.Days
            ? start.AddDays(value - 1)
            : NextCycleStart(start, value, cycleDay ?? start.Day).AddDays(-1);
    }

    /// <summary>
    /// Día del ciclo de una membresía en meses. Se hereda el de la membresía anterior solo si la
    /// nueva empieza justo en ese día del ciclo (ajustado al último día del mes cuando no existe);
    /// si no (p. ej. el fin se corrió por un congelamiento), el ciclo arranca en el día de inicio.
    /// </summary>
    public static int ResolveCycleDay(DateOnly start, int? inheritedCycleDay)
    {
        if (inheritedCycleDay is { } inherited
            && Math.Min(inherited, DateTime.DaysInMonth(start.Year, start.Month)) == start.Day)
        {
            return inherited;
        }

        return start.Day;
    }

    /// <summary>Inicio del ciclo siguiente: <paramref name="months"/> meses después, en el día del ciclo o el último del mes.</summary>
    public static DateOnly NextCycleStart(DateOnly start, int months, int cycleDay)
    {
        var firstOfTarget = new DateOnly(start.Year, start.Month, 1).AddMonths(months);
        var day = Math.Min(cycleDay, DateTime.DaysInMonth(firstOfTarget.Year, firstOfTarget.Month));
        return new DateOnly(firstOfTarget.Year, firstOfTarget.Month, day);
    }

    /// <summary>
    /// Fin de una membresía nueva y su día de ciclo (nulo en planes por días), dado el ciclo que
    /// hereda de la anterior encadenada.
    /// </summary>
    public static (DateOnly EndDate, int? CycleDay) Resolve(
        DateOnly start, DurationUnit unit, int value, int? inheritedCycleDay)
    {
        int? cycleDay = unit == DurationUnit.Months ? ResolveCycleDay(start, inheritedCycleDay) : null;
        return (ComputeEnd(start, unit, value, cycleDay), cycleDay);
    }
}
