using GSM.Application.Attendance;
using GSM.Application.Attendance.Public;
using GSM.Application.Routines.Public;

namespace GSM.Infrastructure.Achievements;

/// <summary>
/// Métricas que puntúan logros. La racha se mide contra el calendario de la rutina **asignada**
/// (las auto-asignadas no cuentan); sin rutina asignada vigente cae a la regla clásica.
/// Los congeladores no aplican a la racha que puntúa logros: aquí es estricta.
/// </summary>
internal static class AchievementMetrics
{
    public static async Task<(int Visits, int Streak, int RoutineSessions)> ComputeAsync(
        Guid memberId,
        DateOnly today,
        IAttendanceReader attendance,
        IRoutineReader routines,
        CancellationToken cancellationToken)
    {
        var visits = await attendance.GetVisitCountAsync(memberId, cancellationToken);
        var sessions = await routines.GetCompletedSessionCountAsync(memberId, onlyAssigned: true, cancellationToken);

        int streak;
        var start = await routines.GetActiveRoutineStartAsync(memberId, onlyAssigned: true, cancellationToken);
        if (start is { } s && s <= today)
        {
            var calendar = await routines.GetTrainingCalendarAsync(
                memberId, s, today, onlyAssigned: true, cancellationToken);
            var attended = (await attendance.GetAttendanceDatesAsync(memberId, s, cancellationToken)).ToHashSet();
            streak = StreakCalculator.ComputeAgainstCalendar(
                attended, calendar.TrainingDays, calendar.RestDays, today);
        }
        else
        {
            streak = await attendance.GetStreakAsync(memberId, cancellationToken);
        }

        return (visits, streak, sessions);
    }
}
