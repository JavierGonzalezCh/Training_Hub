namespace GSM.Application.Attendance;

/// <summary>
/// Racha = días de asistencia en la corrida ininterrumpida más reciente, con **1 día de descanso
/// de tolerancia**: dos días de asistencia siguen en la misma corrida si están a ≤ 2 días de
/// calendario. La corrida está viva hoy si la última asistencia fue hace ≤ 2 días; si no, la racha es 0.
/// </summary>
public static class StreakCalculator
{
    private const int MaxGapDays = 2;

    public static int Compute(IEnumerable<DateOnly> attendanceDates, DateOnly today)
    {
        var days = attendanceDates.Distinct().OrderByDescending(d => d).ToList();
        if (days.Count == 0)
        {
            return 0;
        }

        // ¿La corrida sigue viva hoy?
        if (today.DayNumber - days[0].DayNumber > MaxGapDays)
        {
            return 0;
        }

        var streak = 1;
        for (var i = 1; i < days.Count; i++)
        {
            if (days[i - 1].DayNumber - days[i].DayNumber > MaxGapDays)
            {
                break;
            }

            streak++;
        }

        return streak;
    }

    /// <summary>
    /// Racha contra el calendario de una rutina: cuenta **días de calendario cumplidos** hacia atrás
    /// desde hoy. Un descanso planificado suma igual que un entrenamiento asistido; un día de
    /// entrenamiento sin asistencia corta (salvo que <paramref name="freezeOn"/> lo cubra).
    /// El día en curso nunca penaliza. El recorrido se detiene en el primer día no cubierto por
    /// una rutina (típicamente <c>starts_on</c> de la más antigua).
    /// </summary>
    /// <param name="attendanceDates">Días con asistencia.</param>
    /// <param name="trainingDays">Días en que tocaba entrenar.</param>
    /// <param name="restDays">Días de descanso planificado.</param>
    /// <param name="today">Día de negocio actual.</param>
    /// <param name="freezeOn">
    /// Dado un día de entrenamiento fallado, devuelve <c>true</c> si un congelador lo cubre (y lo
    /// consume). Por defecto nunca congela.
    /// </param>
    public static int ComputeAgainstCalendar(
        IReadOnlySet<DateOnly> attendanceDates,
        IReadOnlySet<DateOnly> trainingDays,
        IReadOnlySet<DateOnly> restDays,
        DateOnly today,
        Func<DateOnly, bool>? freezeOn = null)
    {
        freezeOn ??= static _ => false;

        // El día en curso nunca penaliza: si hoy toca entrenar y aún no hay asistencia, arranca ayer.
        var cursor = today;
        if (trainingDays.Contains(today) && !attendanceDates.Contains(today))
        {
            cursor = today.AddDays(-1);
        }

        var streak = 0;
        while (true)
        {
            if (restDays.Contains(cursor))
            {
                streak++;
            }
            else if (trainingDays.Contains(cursor))
            {
                if (attendanceDates.Contains(cursor))
                {
                    streak++;
                }
                else if (freezeOn(cursor))
                {
                    // Congelador consumido: el día no suma pero tampoco corta.
                }
                else
                {
                    break;
                }
            }
            else
            {
                // Fuera de toda rutina vigente: fin del recorrido.
                break;
            }

            cursor = cursor.AddDays(-1);
        }

        return streak;
    }
}
