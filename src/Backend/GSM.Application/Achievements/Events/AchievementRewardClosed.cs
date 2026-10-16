using GSM.Application.Common.Events;

namespace GSM.Application.Achievements.Events;

/// <summary>
/// Un premio dejó de estar por reclamar (L5): se entregó, o el logro manual se retiró. Notifications cancela con
/// él el aviso de vencimiento que aún no haya salido.
/// </summary>
public sealed record AchievementRewardClosed(
    Guid MemberAchievementId, Guid MemberId, Guid GymId, Guid AchievementId) : IDomainEvent;
