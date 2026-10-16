using GSM.Domain.Achievements;

namespace GSM.Application.Achievements.Public;

public interface IAchievementReader
{
    /// <summary>Todos los logros activos del gym con el estado del miembro (desbloqueado + progreso).</summary>
    Task<IReadOnlyList<MemberAchievementView>> GetForMemberAsync(
        Guid memberId, Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>Nombre del logro (para los avisos de Comunicaciones). Nulo si no existe.</summary>
    Task<string?> GetAchievementNameAsync(Guid achievementId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Premios que el miembro aún puede reclamar: pendientes, con el plazo vigente y de logros no retirados (L5).
    /// Para la tarjeta de recepción de Attendance. Solo lectura.
    /// </summary>
    Task<IReadOnlyList<PendingRewardView>> GetPendingRewardsAsync(
        Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>El premio de un desbloqueo concreto (para programar el aviso de vencimiento). Nulo si no existe o no tiene premio.</summary>
    Task<PendingRewardView?> GetRewardAsync(Guid memberAchievementId, CancellationToken cancellationToken = default);
}

public sealed record MemberAchievementView(
    Guid AchievementId,
    string Name,
    string? Description,
    string? IconUrl,
    AchievementConditionType ConditionType,
    int ConditionValue,
    bool Unlocked,
    DateTimeOffset? UnlockedAt,
    int Progress,
    bool IsManual,
    string? GrantReason,
    string? RewardDescription,
    RewardStatus RewardStatus,
    DateTimeOffset? RewardExpiresAt,
    DateTimeOffset? RewardDeliveredAt);

/// <summary>Un premio por reclamar (L5).</summary>
public sealed record PendingRewardView(
    Guid MemberAchievementId,
    Guid MemberId,
    Guid GymId,
    Guid AchievementId,
    string AchievementName,
    string RewardDescription,
    DateTimeOffset? ExpiresAt);
