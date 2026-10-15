using GSM.Application.Attendance;
using GSM.Application.Attendance.Public;
using GSM.Application.Classes.Public;
using GSM.Application.Routines.Public;

namespace GSM.Infrastructure.Achievements;

/// <summary>
/// Métricas que puntúan logros. La racha se mide contra el calendario de la rutina **asignada**
/// (las auto-asignadas no cuentan); sin rutina asignada vigente cae a la regla clásica.
/// Los congeladores no aplican a la racha que puntúa logros: aquí es estricta.
/// </summary>
internal static class AchievementMetrics
{
    public static async Task<(int Visits, int Streak, int RoutineSessions, int ClassAttendance)> ComputeAsync(
        Guid memberId,
        DateOnly today,
        IAttendanceReader attendance,
        IRoutineReader routines,
        IClassReader classes,
        CancellationToken cancellationToken)
    {
        var visits = await attendance.GetVisitCountAsync(memberId, cancellationToken);
        var sessions = await routines.GetCompletedSessionCountAsync(memberId, onlyAssigned: true, cancellationToken);
        var classAttendance = await classes.GetAttendedCountAsync(memberId, cancellationToken);

        int streak;
        var activeStart = await routines.GetActiveRoutineStartAsync(memberId, onlyAssigned: true, cancellationToken);
        var start = activeStart is null
            ? null
            : await routines.GetCalendarStartAsync(memberId, onlyAssigned: true, cancellationToken);
        if (activeStart is { } a && a <= today && start is { } s)
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

        return (visits, streak, sessions, classAttendance);
    }
}
