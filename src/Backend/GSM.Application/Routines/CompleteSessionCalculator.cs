namespace GSM.Application.Routines;

/// <summary>Un ejercicio registrado: qué línea de la rutina y qué día de negocio.</summary>
public sealed record LoggedLine(DateOnly PerformedOn, Guid RoutineExerciseId);

/// <summary>Una línea de un día de la rutina con su vigencia (desde que se creó hasta que se retiró).</summary>
public sealed record DayLine(Guid Id, Guid RoutineDayId, DateOnly AddedOn, DateOnly? RetiredOn)
{
    public bool InForce(DateOnly date) => AddedOn <= date && (RetiredOn is null || date < RetiredOn);
}

/// <summary>
/// «Sesiones completas» para los logros de disciplina: una fecha cuenta si en ella el miembro registró todas las
/// líneas vigentes de algún día de su rutina (el que le tocaba u otro del plan). Función pura.
/// </summary>
public static class CompleteSessionCalculator
{
    public static int Count(IReadOnlyList<LoggedLine> logs, IReadOnlyList<DayLine> lines)
    {
        var byDay = lines.GroupBy(l => l.RoutineDayId).ToDictionary(g => g.Key, g => g.ToList());
        var lineDay = lines.ToDictionary(l => l.Id, l => l.RoutineDayId);

        var complete = 0;
        foreach (var date in logs.GroupBy(l => l.PerformedOn))
        {
            var logged = date.Select(l => l.RoutineExerciseId).ToHashSet();
            var days = logged.Where(lineDay.ContainsKey).Select(id => lineDay[id]).Distinct();
            if (days.Any(dayId => byDay[dayId].Where(l => l.InForce(date.Key)) is var due
                && due.Any() && due.All(l => logged.Contains(l.Id))))
            {
                complete++;
            }
        }

        return complete;
    }
}
