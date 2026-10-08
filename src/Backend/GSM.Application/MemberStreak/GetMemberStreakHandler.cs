using GSM.Application.Achievements.Public;
using GSM.Application.Attendance;
using GSM.Application.Attendance.Public;
using GSM.Application.Common.Abstractions;
using GSM.Application.Members.Public;
using GSM.Application.Routines.Public;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Identity;

namespace GSM.Application.MemberStreak;

/// <summary>
/// Caso de uso compositor (precedente: Dashboard). Junta <see cref="IAttendanceReader"/>,
/// <see cref="IRoutineReader"/> y <see cref="IAchievementService"/> sin pertenecer a ninguno:
/// <c>Attendance</c> no puede depender de <c>Routines</c>. Con rutina activa la racha se mide
/// contra su calendario y los congeladores cubren los fallos; sin ella, se mantiene exactamente
/// la regla del <see cref="StreakCalculator"/> de siempre.
/// </summary>
/// <remarks>Consultar la racha puede escribir (concede o consume congeladores). Es deliberado.</remarks>
public sealed class GetMemberStreakHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberReader members,
    IAttendanceReader attendance,
    IRoutineReader routines,
    IAchievementService achievements)
{
    public async Task<MemberStreakResponse> HandleAsync(Guid memberId, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        if (currentUser.Role == UserRole.Member)
        {
            var self = await members.ResolveByUserIdAsync(currentUser.UserId ?? Guid.Empty, cancellationToken);
            if (self is null || self.Id != memberId)
            {
                throw new ForbiddenDomainException("No puedes ver la racha de otro miembro.");
            }
        }
        else if (!await members.ExistsAsync(memberId, cancellationToken))
        {
            throw new NotFoundException("Miembro no encontrado.");
        }

        var today = clock.Today;

        // Concesión perezosa: aplica también a miembros sin rutina.
        var freezeState = await achievements.GetOrGrantStreakFreezesAsync(memberId, today, cancellationToken);

        var (streak, freezesAvailable) = await ComputeStreakAsync(
            memberId, today, freezeState, cancellationToken);

        var visits = await attendance.GetVisitCountAsync(memberId, cancellationToken);
        var last = await attendance.GetLastVisitAsync(memberId, cancellationToken);

        return new MemberStreakResponse(memberId, streak, visits, last)
        {
            FreezesAvailable = freezesAvailable,
        };
    }

    private async Task<(int Streak, int FreezesAvailable)> ComputeStreakAsync(
        Guid memberId, DateOnly today, StreakFreezeState freezeState, CancellationToken cancellationToken)
    {
        var routineStart = await routines.GetActiveRoutineStartAsync(
            memberId, onlyAssigned: false, cancellationToken);

        if (routineStart is not { } start || start > today)
        {
            // Sin rutina vigente: regla de siempre (tolerancia de días de descanso), sin congeladores.
            var allDates = await attendance.GetAttendanceDatesAsync(memberId, null, cancellationToken);
            return (StreakCalculator.Compute(allDates, today), freezeState.Available);
        }

        var calendar = await routines.GetTrainingCalendarAsync(
            memberId, start, today, onlyAssigned: false, cancellationToken);
        var attended = (await attendance.GetAttendanceDatesAsync(memberId, start, cancellationToken)).ToHashSet();

        var spent = new HashSet<DateOnly>(freezeState.SpentOn);
        var newlyConsumed = new List<DateOnly>();

        var streak = StreakCalculator.ComputeAgainstCalendar(
            attended, calendar.TrainingDays, calendar.RestDays, today,
            freezeOn: failedDate =>
            {
                if (spent.Contains(failedDate))
                {
                    return true;
                }

                if (freezeState.Available - newlyConsumed.Count > 0)
                {
                    newlyConsumed.Add(failedDate);
                    spent.Add(failedDate);
                    return true;
                }

                return false;
            });

        var freezesAvailable = newlyConsumed.Count > 0
            ? await achievements.ConsumeStreakFreezesAsync(memberId, newlyConsumed, cancellationToken)
            : freezeState.Available;

        return (streak, freezesAvailable);
    }
}
