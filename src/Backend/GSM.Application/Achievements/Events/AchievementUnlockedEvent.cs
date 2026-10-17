using GSM.Application.Common.Events;

namespace GSM.Application.Achievements.Events;

/// <summary>
/// Se publica al desbloquear un logro. <paramref name="IsManual"/> distingue el logro que otorga el staff (L4-5):
/// su aviso dice "Te otorgaron" en lugar de "Desbloqueaste".
/// </summary>
public sealed record AchievementUnlockedEvent(
    Guid MemberAchievementId, Guid MemberId, Guid GymId, Guid AchievementId, bool IsManual = false) : IDomainEvent;
