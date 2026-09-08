using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Achievements;

/// <summary>Logro desbloqueado por un miembro. Único por <c>(member_id, achievement_id)</c>.</summary>
public sealed class MemberAchievement : BaseEntity, ITenantEntity
{
    private MemberAchievement()
    {
    }

    public Guid GymId { get; private set; }

    public Guid MemberId { get; private set; }

    public Guid AchievementId { get; private set; }

    public DateTimeOffset UnlockedAt { get; private set; }

    public static MemberAchievement Unlock(
        Guid gymId, Guid memberId, Guid achievementId, DateTimeOffset unlockedAt)
    {
        if (gymId == Guid.Empty || memberId == Guid.Empty || achievementId == Guid.Empty)
        {
            throw new ValidationDomainException("Datos de desbloqueo incompletos.");
        }

        return new MemberAchievement
        {
            GymId = gymId,
            MemberId = memberId,
            AchievementId = achievementId,
            UnlockedAt = unlockedAt,
        };
    }
}
