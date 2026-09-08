using GSM.Application.Common.Events;

namespace GSM.Application.Achievements.Events;

/// <summary>Se publica al desbloquear un logro. Etapa 8 lo usará para push.</summary>
public sealed record AchievementUnlockedEvent(
    Guid MemberAchievementId, Guid MemberId, Guid GymId, Guid AchievementId) : IDomainEvent;
