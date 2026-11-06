using GSM.Application.Routines.Abstractions;
using GSM.Domain.Routines;

namespace GSM.Application.Routines.MissedSession;

/// <summary>Una fecha de lo que queda de la semana, con el día de la rutina que cuenta en ella.</summary>
/// <param name="PlannedDayId">El día que toca según el plan, sin ajustes del miembro.</param>
public sealed record WeekSlot(DateOnly Date, bool IsRest, Guid? DayId, string? DayName, Guid? PlannedDayId);

/// <summary>
/// Una de las dos salidas al recuperar la sesión perdida: cómo quedan los próximos días y qué rutina se pierde
/// (<c>null</c> al correr el ciclo de una rotativa: no se pierde ninguna).
/// </summary>
public sealed record MissedSessionOption(IReadOnlyList<WeekSlot> Week, string? LostDayName, bool ShiftsCycle = false);

/// <summary>
/// La sesión perdida de ayer (SP, docs/GSM-sesion-perdida.md): qué faltó, cómo está la semana y, si hoy toca entrenar,
/// cómo queda con cada decisión. <see cref="Swap"/> y <see cref="Reorganize"/> son nulos si hoy es descanso (solo se
/// sugiere hacerla). En una semanal, reorganizar corre la semana con los descansos fijos; en una rotativa, corre todo
/// el ciclo un día.
/// </summary>
public sealed record MissedSessionPlan(
    AdherenceRoutine Routine,
    DateOnly MissedOn,
    AdherenceDay MissedDay,
    bool TodayIsRest,
    IReadOnlyList<WeekSlot> Before,
    MissedSessionOption? Swap,
    MissedSessionOption? Reorganize);

/// <summary>Un cambio a guardar: en <paramref name="Date"/> cuenta <paramref name="PerformedDayId"/>.</summary>
public sealed record PlannedOverride(DateOnly Date, Guid PlannedDayId, Guid PerformedDayId);

/// <summary>
/// Reglas 2 a 5 de la sesión perdida. Función pura sobre lo ya leído: la vista previa y lo que se guarda salen de la
/// misma cuenta, así que lo que el miembro ve antes de decidir es lo que queda. Qué toca cada fecha lo dice
/// <see cref="RoutineCalendar"/>; aquí no se reimplementa.
/// </summary>
public static class MissedSessionPlanner
{
    /// <summary>Último día de la semana de negocio (domingo) que contiene <paramref name="date"/>.</summary>
    public static DateOnly WeekEndOf(DateOnly date) => date.AddDays(IsoWeekDay.Sunday - IsoWeekDay.Of(date));

    public static string NameOf(AdherenceDay day)
        => day.Name ?? (day.DayOfWeek is { } weekday ? RoutineMapper.WeekDayName(weekday) : $"Día {day.Position}");

    /// <summary>
    /// La sesión perdida de ayer, o nulo si no hay nada que ofrecer: ayer no tocaba entrenar, ayer registró algo de ese
    /// día, la rutina cambió, el día perdido ya no está vigente, hoy ya registró algo o hoy ya hay un cambio decidido.
    /// </summary>
    /// <param name="ordered">Rutinas del miembro ya ordenadas con <see cref="RoutineCalendar.Order"/>.</param>
    public static MissedSessionPlan? Detect(
        IReadOnlyList<AdherenceRoutine> ordered,
        IReadOnlyList<AdherenceLog> logs,
        IReadOnlyDictionary<DateOnly, AdherenceOverride> overrides,
        DateOnly today)
    {
        var yesterday = today.AddDays(-1);
        var missed = RoutineCalendar.Resolve(ordered, yesterday, overrides);
        if (!missed.IsTraining)
        {
            return null;
        }

        var missedDay = missed.Day!;
        var lines = missedDay.Lines.Select(l => l.Id).ToHashSet();
        if (logs.Any(l => l.PerformedOn == yesterday && lines.Contains(l.RoutineExerciseId)))
        {
            return null;
        }

        // Ya decidió (hay un cambio hoy) o ya empezó a entrenar hoy: la perdida queda perdida (4a).
        if (overrides.ContainsKey(today) || logs.Any(l => l.PerformedOn == today))
        {
            return null;
        }

        var current = RoutineCalendar.Resolve(ordered, today, overrides);
        if (current.Routine is null || current.Routine.Id != missed.Routine!.Id
            || !RoutineCalendar.InForce(missedDay, today) || current.Day?.Id == missedDay.Id)
        {
            return null;
        }

        var rotating = current.Routine.Mode == ScheduleMode.Rotating;
        // Semanal: hasta el domingo (la regla 5 es dentro de la semana). Rotativa: los próximos 7 días.
        var until = rotating ? today.AddDays(6) : WeekEndOf(today);
        var before = Week(ordered, overrides, today, until);
        if (!current.IsTraining)
        {
            return new MissedSessionPlan(current.Routine, yesterday, missedDay, true, before, null, null);
        }

        // 4b-no: hoy se hace la perdida; la de hoy queda perdida y el resto de la semana no cambia.
        var swapped = before.ToList();
        swapped[0] = swapped[0] with { DayId = missedDay.Id, DayName = NameOf(missedDay) };
        var swap = new MissedSessionOption(swapped, NameOf(current.Day!));

        MissedSessionOption reorganize;
        if (rotating)
        {
            // Rotativa: todo el ciclo se corre un día (también los descansos); no se pierde ninguna.
            var withShift = new Dictionary<DateOnly, AdherenceOverride>(overrides)
            {
                [today] = new AdherenceOverride(
                    current.Routine.MemberId, current.Routine.Id, today, current.Day!.Id, missedDay.Id,
                    RoutineDayOverrideReason.Shift),
            };
            reorganize = new MissedSessionOption(Week(ordered, withShift, today, until), null, ShiftsCycle: true);
        }
        else
        {
            // Regla 5: los descansos quedan fijos y las rutinas se corren en orden dentro de la semana; la última no cabe.
            var moved = before.ToList();
            var carry = (Id: (Guid?)missedDay.Id, Name: (string?)NameOf(missedDay));
            for (var i = 0; i < moved.Count; i++)
            {
                if (moved[i].IsRest)
                {
                    continue;
                }

                var displaced = (Id: moved[i].DayId, Name: moved[i].DayName);
                moved[i] = moved[i] with { DayId = carry.Id, DayName = carry.Name };
                carry = displaced;
            }

            reorganize = new MissedSessionOption(moved, carry.Name ?? "?");
        }

        return new MissedSessionPlan(current.Routine, yesterday, missedDay, false, before, swap, reorganize);
    }

    /// <summary>
    /// Los cambios a guardar para una opción: una fila por fecha cuyo día cambia respecto del plan. Una fecha que con la
    /// opción vuelve a su día planeado no lleva fila (el llamador borra la que hubiera).
    /// </summary>
    public static IReadOnlyList<PlannedOverride> Changes(MissedSessionOption option)
        => option.Week
            .Where(s => !s.IsRest && s.DayId is { } performed && s.PlannedDayId is { } planned && performed != planned)
            .Select(s => new PlannedOverride(s.Date, s.PlannedDayId!.Value, s.DayId!.Value))
            .ToList();

    /// <summary>De hoy a <paramref name="until"/>, con lo que cuenta en cada fecha según <see cref="RoutineCalendar"/>.</summary>
    private static List<WeekSlot> Week(
        IReadOnlyList<AdherenceRoutine> ordered,
        IReadOnlyDictionary<DateOnly, AdherenceOverride> overrides,
        DateOnly today,
        DateOnly until)
    {
        var slots = new List<WeekSlot>(7);
        for (var date = today; date <= until; date = date.AddDays(1))
        {
            var resolution = RoutineCalendar.Resolve(ordered, date, overrides);
            var planned = resolution.PlannedDay ?? resolution.Day;
            slots.Add(resolution.IsTraining
                ? new WeekSlot(date, false, resolution.Day!.Id, NameOf(resolution.Day), planned!.Id)
                : new WeekSlot(date, true, null, resolution.Day is { } rest ? NameOf(rest) : "Descanso", null));
        }

        return slots;
    }
}
