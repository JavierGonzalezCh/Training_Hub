using GSM.Application.Achievements.Abstractions;
using GSM.Application.Achievements.Events;
using GSM.Application.Achievements.Public;
using GSM.Application.Attendance.Public;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Domain.Achievements;
using Microsoft.Extensions.Logging;

namespace GSM.Infrastructure.Achievements;

/// <summary>
/// Evalúa y desbloquea los logros pendientes de un miembro. Idempotente: solo mira los que
/// aún no tiene. Se llama de forma síncrona tras un check-in.
/// </summary>
public sealed class AchievementService(
    IClock clock,
    IAchievementRepository achievements,
    IAttendanceReader attendance,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<AchievementService> logger) : IAchievementService
{
    public async Task<IReadOnlyList<UnlockedAchievement>> EvaluateForMemberAsync(
        Guid memberId, CancellationToken cancellationToken = default)
    {
        var pending = await achievements.ListPendingForMemberAsync(memberId, cancellationToken);
        if (pending.Count == 0)
        {
            return [];
        }

        var visits = await attendance.GetVisitCountAsync(memberId, cancellationToken);
        var streak = await attendance.GetStreakAsync(memberId, cancellationToken);
        var now = clock.UtcNow;

        var newlyUnlocked = new List<(MemberAchievement Row, Achievement Achievement)>();
        foreach (var achievement in pending.Where(a => a.IsMetBy(visits, streak)))
        {
            var row = MemberAchievement.Unlock(achievement.GymId, memberId, achievement.Id, now);
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
}
