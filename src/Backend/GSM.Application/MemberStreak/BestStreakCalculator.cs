namespace GSM.Application.MemberStreak;

/// <summary>
/// La mejor racha histórica (L7), calculada sobre lo que ya hay —asistencias, calendario de la rutina y los congeladores
/// gastados— sin guardar nada más. Aplica la misma regla que la racha actual: antes de que existiera una rutina vale la
/// tolerancia de días de descanso; desde el inicio de su calendario, un descanso planificado suma, un día de
/// entrenamiento fallado corta (salvo que un congelador lo cubriera) y el día en curso nunca penaliza.
/// </summary>
public static class BestStreakCalculator
{
    private const int MaxGapDays = 2;

    /// <param name="calendarStart">Inicio del calendario de la rutina; nulo si el miembro no tiene rutina vigente.</param>
    public static int Compute(
        IReadOnlyCollection<DateOnly> attendanceDates,
        DateOnly? calendarStart,
        IReadOnlySet<DateOnly> trainingDays,
        IReadOnlySet<DateOnly> restDays,
        IReadOnlySet<DateOnly> frozenDays,
        DateOnly today)
    {
        var attended = attendanceDates.ToHashSet();

        // Tramo sin calendario: la regla de siempre (corridas con huecos de a lo sumo un día de descanso).
        var classic = attended.Where(d => calendarStart is null || d < calendarStart).OrderBy(d => d).ToList();
        var best = 0;
        var run = 0;
        DateOnly? previous = null;
        foreach (var day in classic)
        {
            run = previous is { } p && day.DayNumber - p.DayNumber <= MaxGapDays ? run + 1 : 1;
            best = Math.Max(best, run);
            previous = day;
        }

        if (calendarStart is not { } start)
        {
            return best;
        }

        run = 0;
        for (var day = start; day <= today; day = day.AddDays(1))
        {
            if (restDays.Contains(day))
            {
                run++;
            }
            else if (trainingDays.Contains(day))
            {
                if (attended.Contains(day))
                {
                    run++;
                }
                else if (frozenDays.Contains(day) || day == today)
                {
                    // Cubierto por un congelador, o el día en curso: ni suma ni corta.
                }
                else
                {
                    best = Math.Max(best, run);
                    run = 0;
                }
            }
            else
            {
                // Fuera de toda rutina vigente: fin del recorrido.
                best = Math.Max(best, run);
                run = 0;
            }

            best = Math.Max(best, run);
        }

        return best;
    }
}
