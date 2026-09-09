using GSM.Application.Attendance;
using GSM.Application.Attendance.Public;
using GSM.Application.Common.Abstractions;
using GSM.Application.Members.Public;
using GSM.Application.Routines.Public;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Identity;

namespace GSM.Application.MemberStreak;

/// <summary>
/// Caso de uso compositor (precedente: Dashboard). Junta <see cref="IAttendanceReader"/> y
/// <see cref="IRoutineReader"/> sin pertenecer a ninguno: <c>Attendance</c> no puede depender de
/// <c>Routines</c>. Con rutina activa, la racha se mide contra su calendario; sin ella, se mantiene
/// exactamente la regla del <see cref="StreakCalculator"/> de siempre.
/// </summary>
public sealed class GetMemberStreakHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberReader members,
    IAttendanceReader attendance,
    IRoutineReader routines)
{
    public async Task<MemberStreakResponse> HandleAsync(Guid memberId, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        if (currentUser.Role == UserRole.Member)
        {
            var self = await members.ResolveByUserIdAsync(currentUser.UserId ?? Guid.Empty, cancellationToken);
            if (self is null || self.Id != memberId)
            {
                throw new ForbiddenDomainException("No puedes ver la racha de otro socio.");
            }
        }
        else if (!await members.ExistsAsync(memberId, cancellationToken))
        {
            throw new NotFoundException("Miembro no encontrado.");
        }

        var today = clock.Today;
        var streak = await ComputeStreakAsync(memberId, today, cancellationToken);

        var visits = await attendance.GetVisitCountAsync(memberId, cancellationToken);
        var last = await attendance.GetLastVisitAsync(memberId, cancellationToken);

        return new MemberStreakResponse(memberId, streak, visits, last);
    }

    private async Task<int> ComputeStreakAsync(Guid memberId, DateOnly today, CancellationToken cancellationToken)
    {
        var routineStart = await routines.GetActiveRoutineStartAsync(memberId, cancellationToken);

        if (routineStart is not { } start || start > today)
        {
            // Sin rutina vigente: regla de siempre (tolerancia de días de descanso).
            var allDates = await attendance.GetAttendanceDatesAsync(memberId, null, cancellationToken);
            return StreakCalculator.Compute(allDates, today);
        }

        var calendar = await routines.GetTrainingCalendarAsync(memberId, start, today, cancellationToken);
        var attended = (await attendance.GetAttendanceDatesAsync(memberId, start, cancellationToken)).ToHashSet();

        return StreakCalculator.ComputeAgainstCalendar(
            attended, calendar.TrainingDays, calendar.RestDays, today);
    }
}
