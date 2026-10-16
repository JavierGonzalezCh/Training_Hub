using GSM.Application.Achievements.Abstractions;
using GSM.Application.Achievements.Events;
using GSM.Application.Achievements.Public;
using GSM.Application.Attendance.Public;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Domain.Achievements;
using GSM.Domain.Common.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GSM.Infrastructure.Achievements;

/// <summary>
/// Evalúa y desbloquea los logros pendientes de un miembro. Idempotente: solo mira los que
/// aún no tiene. Se llama de forma síncrona tras un check-in.
/// </summary>
public sealed class AchievementService(
    IClock clock,
    ICurrentUserService currentUser,
    IAchievementRepository achievements,
    IAttendanceReader attendance,
    GSM.Application.Routines.Public.IRoutineReader routines,
    GSM.Application.Classes.Public.IClassReader classes,
    GSM.Application.Members.Public.IMemberReader members,
    GSM.Application.Memberships.Public.IMembershipReader memberships,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<AchievementService> logger) : IAchievementService
{
    private const int MaxAvailableFreezes = 2;
    private const int GrantIntervalDays = 15;


    public async Task<IReadOnlyList<UnlockedAchievement>> EvaluateForMemberAsync(
        Guid memberId, CancellationToken cancellationToken = default)
    {
        var pending = await achievements.ListPendingForMemberAsync(memberId, cancellationToken);
        if (pending.Count == 0)
        {
            return [];
        }

        var (visits, streak, sessions, classAttendance, tenure, referrals) = await AchievementMetrics.ComputeAsync(
            memberId, clock.Today, attendance, routines, classes, members, memberships,
            needTenure: pending.Any(a => a.ConditionType == AchievementConditionType.TenureMonths),
            needReferrals: pending.Any(a => a.ConditionType == AchievementConditionType.Referrals),
            cancellationToken);
        var now = clock.UtcNow;

        var newlyUnlocked = new List<(MemberAchievement Row, Achievement Achievement)>();
        foreach (var achievement in pending.Where(a => a.IsMetBy(visits, streak, sessions, classAttendance, tenure, referrals)))
        {
            var row = MemberAchievement.Unlock(
                achievement.GymId, memberId, achievement.Id, now, achievement.RewardDescription, achievement.RewardValidDays);
            achievements.AddUnlock(row);
            newlyUnlocked.Add((row, achievement));
        }

        if (newlyUnlocked.Count == 0)
        {
            return [];
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Miembro {MemberId} desbloqueó {Count} logro(s)", memberId, newlyUnlocked.Count);

        foreach (var (row, achievement) in newlyUnlocked)
        {
            await events.PublishAsync(
                new AchievementUnlockedEvent(row.Id, memberId, achievement.GymId, achievement.Id),
                cancellationToken);
        }

        return newlyUnlocked
            .Select(x => new UnlockedAchievement(x.Achievement.Id, x.Achievement.Name, x.Achievement.IconUrl, now))
            .ToList();
    }

    public async Task<StreakFreezeState> GetOrGrantStreakFreezesAsync(
        Guid memberId, DateOnly today, CancellationToken cancellationToken = default)
    {
        var freezes = await achievements.ListStreakFreezesAsync(memberId, cancellationToken);
        var available = freezes.Count(f => f.IsAvailable);
        var spent = freezes.Where(f => f.UsedOn is not null).Select(f => f.UsedOn!.Value).ToHashSet();

        if (available < MaxAvailableFreezes)
        {
            var lastGranted = freezes.Count == 0 ? (DateOnly?)null : freezes.Max(f => f.GrantedOn);
            if (lastGranted is null || today.DayNumber - lastGranted.Value.DayNumber >= GrantIntervalDays)
            {
                var gymId = currentUser.RequireGymId();
                achievements.AddStreakFreeze(StreakFreeze.Grant(gymId, memberId, today));
                await unitOfWork.SaveChangesAsync(cancellationToken);
                available++;
                logger.LogInformation("Congelador de racha otorgado al miembro {MemberId}", memberId);
            }
        }

        return new StreakFreezeState(available, spent);
    }

    public async Task<int> ConsumeStreakFreezesAsync(
        Guid memberId, IReadOnlyCollection<DateOnly> failedDates, CancellationToken cancellationToken = default)
    {
        var freezes = await achievements.ListStreakFreezesAsync(memberId, cancellationToken);

        if (failedDates.Count > 0)
        {
            var spent = freezes.Where(f => f.UsedOn is not null).Select(f => f.UsedOn!.Value).ToHashSet();
            var queue = new Queue<StreakFreeze>(freezes.Where(f => f.IsAvailable).OrderBy(f => f.GrantedOn));

            foreach (var date in failedDates.Distinct())
            {
                if (spent.Contains(date) || queue.Count == 0)
                {
                    continue;
                }

                queue.Dequeue().Use(date);
                spent.Add(date);
            }

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                // Carrera: otra consulta concurrente ya marcó un congelador para esa fecha.
                logger.LogWarning(ex, "Consumo de congelador en conflicto para el miembro {MemberId}", memberId);
            }
        }

        return (await achievements.ListStreakFreezesAsync(memberId, cancellationToken)).Count(f => f.IsAvailable);
    }
}
