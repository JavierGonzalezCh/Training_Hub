using GSM.Application.Achievements.Abstractions;
using GSM.Application.Achievements.Events;
using GSM.Application.Attendance.Public;
using GSM.Application.Classes.Public;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Members.Public;
using GSM.Application.Memberships.Public;
using GSM.Application.Routines.Public;
using GSM.Domain.Achievements;
using Microsoft.Extensions.Logging;

namespace GSM.Infrastructure.Achievements;

/// <summary>
/// Logros retroactivos (L1). Las visitas se resuelven en bloque (una consulta para todo el gym); racha,
/// sesiones de rutina y clases usan los lectores existentes miembro por miembro, y solo la métrica que el
/// logro necesita (no las cuatro).
/// </summary>
public sealed class RetroactiveAchievementGrant(
    IClock clock,
    ICurrentUserService currentUser,
    IMemberReader members,
    IMembershipReader memberships,
    IAchievementRepository achievements,
    IAttendanceReader attendance,
    IRoutineReader routines,
    IClassReader classes,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<RetroactiveAchievementGrant> logger) : IRetroactiveAchievementGrant
{
    public async Task<int> CountEligibleAsync(
        AchievementConditionType conditionType, int conditionValue, Guid? achievementId,
        CancellationToken cancellationToken = default)
    {
        var gymId = currentUser.RequireGymId();

        // Un logro de paso solo para reutilizar IsMetBy: no se persiste.
        var probe = Achievement.Create(gymId, "preview", null, null, conditionType, conditionValue);
        return (await FindEligibleAsync(probe, achievementId, cancellationToken)).Count;
    }

    public async Task<int> GrantAsync(Achievement achievement, CancellationToken cancellationToken = default)
    {
        var eligible = await FindEligibleAsync(achievement, achievement.Id, cancellationToken);
        if (eligible.Count == 0)
        {
            return 0;
        }

        var now = clock.UtcNow;
        var rows = eligible
            .Select(memberId => MemberAchievement.Unlock(
                achievement.GymId, memberId, achievement.Id, now, achievement.RewardDescription, achievement.RewardValidDays))
            .ToList();
        foreach (var row in rows)
        {
            achievements.AddUnlock(row);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Logro {AchievementId} otorgado retroactivamente a {Count} miembro(s)", achievement.Id, rows.Count);

        foreach (var row in rows)
        {
            await events.PublishAsync(
                new AchievementUnlockedEvent(row.Id, row.MemberId, achievement.GymId, achievement.Id),
                cancellationToken);
        }

        return rows.Count;
    }

    private async Task<IReadOnlyList<Guid>> FindEligibleAsync(
        Achievement achievement, Guid? alreadyUnlockedFor, CancellationToken cancellationToken)
    {
        // Un logro manual no tiene condición automática: nunca se otorga de forma retroactiva (L4).
        if (achievement.ConditionType == AchievementConditionType.Manual)
        {
            return [];
        }

        var gymId = currentUser.RequireGymId();
        var active = await members.GetActiveMemberIdsAsync(gymId, cancellationToken);
        var have = alreadyUnlockedFor is { } id
            ? await achievements.GetMemberIdsWithUnlockAsync(id, cancellationToken)
            : new HashSet<Guid>();
        var candidates = active.Where(m => !have.Contains(m)).ToList();
        if (candidates.Count == 0)
        {
            return [];
        }

        if (achievement.ConditionType == AchievementConditionType.Visits)
        {
            var visits = await attendance.GetVisitCountsAsync(candidates, cancellationToken);
            return candidates.Where(m => achievement.IsMetBy(visits.GetValueOrDefault(m), 0, 0, 0)).ToList();
        }

        // Antigüedad y referidos también en bloque: una consulta por tipo, no una por miembro.
        if (achievement.ConditionType == AchievementConditionType.TenureMonths)
        {
            var covered = await memberships.GetCoveredDaysAsync(candidates, cancellationToken);
            return candidates
                .Where(m => achievement.IsMetBy(
                    0, 0, 0, 0, tenureMonths: AchievementMetrics.TenureMonthsOf(covered.GetValueOrDefault(m))))
                .ToList();
        }

        if (achievement.ConditionType == AchievementConditionType.Referrals)
        {
            var referred = await members.GetReferredMemberIdsAsync(candidates, cancellationToken);
            var paid = (await memberships.GetMemberIdsWithPaidMembershipAsync(
                referred.Values.SelectMany(v => v).Distinct().ToList(), cancellationToken)).ToHashSet();
            return candidates
                .Where(m => referred.TryGetValue(m, out var ids)
                    && achievement.IsMetBy(0, 0, 0, 0, referrals: ids.Count(paid.Contains)))
                .ToList();
        }

        var eligible = new List<Guid>();
        foreach (var memberId in candidates)
        {
            var met = achievement.ConditionType switch
            {
                AchievementConditionType.Streak => achievement.IsMetBy(
                    0,
                    await AchievementMetrics.ComputeStreakAsync(memberId, clock.Today, attendance, routines, cancellationToken),
                    0, 0),
                AchievementConditionType.RoutineSessions => achievement.IsMetBy(
                    0, 0,
                    await routines.GetCompletedSessionCountAsync(memberId, onlyAssigned: true, cancellationToken),
                    0),
                AchievementConditionType.Classes => achievement.IsMetBy(
                    0, 0, 0, await classes.GetAttendedCountAsync(memberId, cancellationToken)),
                _ => false,
            };

            if (met)
            {
                eligible.Add(memberId);
            }
        }

        return eligible;
    }
}
