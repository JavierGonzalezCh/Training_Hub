using GSM.Application.Attendance;
using GSM.Application.Attendance.Public;
using GSM.Application.Classes.Public;
using GSM.Application.Members.Public;
using GSM.Application.Memberships.Public;
using GSM.Application.Routines.Public;

namespace GSM.Infrastructure.Achievements;

/// <summary>
/// Métricas que puntúan logros. La racha se mide contra el calendario de la rutina **asignada**
/// (las auto-asignadas no cuentan); sin rutina asignada vigente cae a la regla clásica.
/// Los congeladores no aplican a la racha que puntúa logros: aquí es estricta.
/// </summary>
internal static class AchievementMetrics
{
    /// <summary>Días de una membresía que hacen un "mes" de antigüedad (spec §3.1: días cubiertos entre 30).</summary>
    public const int DaysPerTenureMonth = 30;

    /// <summary>Kilos enteros hacia abajo (la condición es un entero), sin desbordar.</summary>
    public static int ToWholeKg(decimal kg) => kg >= int.MaxValue ? int.MaxValue : (int)Math.Floor(kg);

    public static int TenureMonthsOf(int coveredDays) => coveredDays / DaysPerTenureMonth;

    /// <param name="needTenure">Solo se consultan Memberships si hay un logro de antigüedad pendiente.</param>
    /// <param name="needLifting">Solo se leen los registros de series si hay un logro de récords o volumen pendiente.</param>
    /// <param name="needReferrals">Solo se consultan Members y Memberships si hay un logro de referidos pendiente.</param>
    public static async Task<(int Visits, int Streak, int RoutineSessions, int ClassAttendance, int TenureMonths, int Referrals, int PersonalRecords, int LiftedVolumeKg)> ComputeAsync(
        Guid memberId,
        DateOnly today,
        IAttendanceReader attendance,
        IRoutineReader routines,
        IClassReader classes,
        IMemberReader members,
        IMembershipReader memberships,
        bool needTenure,
        bool needReferrals,
        bool needLifting,
        CancellationToken cancellationToken)
    {
        var visits = await attendance.GetVisitCountAsync(memberId, cancellationToken);
        var sessions = await routines.GetCompletedSessionCountAsync(memberId, onlyAssigned: true, cancellationToken);
        var classAttendance = await classes.GetAttendedCountAsync(memberId, cancellationToken);

        var streak = await ComputeStreakAsync(memberId, today, attendance, routines, cancellationToken);

        var tenure = needTenure
            ? TenureMonthsOf(await memberships.GetCoveredDaysAsync(memberId, cancellationToken))
            : 0;
        var referrals = needReferrals ? await CountReferralsAsync(memberId, members, memberships, cancellationToken) : 0;

        var lifting = needLifting
            ? await routines.GetLiftingStatsAsync(memberId, onlyAssigned: true, cancellationToken)
            : new GSM.Application.Routines.LiftingStats(0, 0m);

        return (visits, streak, sessions, classAttendance, tenure, referrals,
            lifting.PersonalRecords, ToWholeKg(lifting.LiftedVolumeKg));
    }

    /// <summary>Referidos de este miembro que ya tienen su primera membresía pagada (spec §3.3).</summary>
    public static async Task<int> CountReferralsAsync(
        Guid memberId, IMemberReader members, IMembershipReader memberships, CancellationToken cancellationToken)
    {
        var referred = await members.GetReferredMemberIdsAsync([memberId], cancellationToken);
        if (!referred.TryGetValue(memberId, out var ids) || ids.Count == 0)
        {
            return 0;
        }

        return (await memberships.GetMemberIdsWithPaidMembershipAsync(ids, cancellationToken)).Count;
    }

    /// <summary>Solo la racha que puntúa logros (estricta, contra el calendario de la rutina asignada).</summary>
    public static async Task<int> ComputeStreakAsync(
        Guid memberId,
        DateOnly today,
        IAttendanceReader attendance,
        IRoutineReader routines,
        CancellationToken cancellationToken)
    {
        var activeStart = await routines.GetActiveRoutineStartAsync(memberId, onlyAssigned: true, cancellationToken);
        var start = activeStart is null
            ? null
            : await routines.GetCalendarStartAsync(memberId, onlyAssigned: true, cancellationToken);
        if (activeStart is { } a && a <= today && start is { } s)
        {
            var calendar = await routines.GetTrainingCalendarAsync(
                memberId, s, today, onlyAssigned: true, cancellationToken);
            var attended = (await attendance.GetAttendanceDatesAsync(memberId, s, cancellationToken)).ToHashSet();
            return StreakCalculator.ComputeAgainstCalendar(
                attended, calendar.TrainingDays, calendar.RestDays, today);
        }

        return await attendance.GetStreakAsync(memberId, cancellationToken);
    }
}
