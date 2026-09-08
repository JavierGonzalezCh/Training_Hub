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
}
