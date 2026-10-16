using GSM.Domain.Achievements;

namespace GSM.Application.Achievements.Contracts;

public sealed record CreateAchievementRequest(
    string Name,
    string? Description,
    string? IconUrl,
    AchievementConditionType ConditionType,
    int ConditionValue,
    string? RewardDescription = null,
    int? RewardValidDays = null);

public sealed record UpdateAchievementRequest(
    string Name,
    string? Description,
    string? IconUrl,
    AchievementConditionType ConditionType,
    int ConditionValue,
    string? RewardDescription = null,
    int? RewardValidDays = null);

public sealed record AchievementResponse(
    Guid Id,
    string Name,
    string? Description,
    string? IconUrl,
    string ConditionType,
    int ConditionValue,
    bool IsActive,
    DateTimeOffset CreatedAt,
    string? RewardDescription,
    int? RewardValidDays);

/// <summary>Condición a evaluar para el aviso previo ("N miembros ya cumplen este logro"). <c>AchievementId</c> excluye a quienes ya lo tienen.</summary>
public sealed record RetroactivePreviewRequest(
    AchievementConditionType ConditionType,
    int ConditionValue,
    Guid? AchievementId);

public sealed record RetroactivePreviewResponse(int EligibleCount);

public sealed record RetroactiveGrantResponse(int GrantedCount);

public sealed record MemberAchievementItem(
    Guid AchievementId,
    string Name,
    string? Description,
    string? IconUrl,
    string ConditionType,
    int ConditionValue,
    bool Unlocked,
    DateTimeOffset? UnlockedAt,
    int Progress,
    bool IsManual,
    string? GrantReason,
    string? RewardDescription,
    string RewardStatus,
    DateTimeOffset? RewardExpiresAt,
    DateTimeOffset? RewardDeliveredAt);

/// <summary>Otorga un logro manual a un miembro (L4). Motivo obligatorio.</summary>
public sealed record GrantManualAchievementRequest(Guid MemberId, string Reason);

/// <summary>Retira un logro manual a un miembro (L4). Motivo obligatorio.</summary>
public sealed record RevokeManualAchievementRequest(string Reason);

/// <summary>Premio entregado en recepción (L5).</summary>
public sealed record RewardDeliveredResponse(
    Guid AchievementId,
    Guid MemberId,
    string AchievementName,
    string RewardDescription,
    DateTimeOffset DeliveredAt);

/// <summary>Un premio ganado por un miembro, para la vista del Owner (L5).</summary>
/// <param name="Status">"Pending", "Delivered" o "Expired" (el vencido se calcula).</param>
public sealed record RewardListItem(
    Guid AchievementId,
    string AchievementName,
    Guid MemberId,
    string MemberName,
    string RewardDescription,
    string Status,
    DateTimeOffset UnlockedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? DeliveredAt,
    Guid? DeliveredBy);

/// <summary>Premios del gym: totales por estado y los más recientes (L5, vista del Owner en Logros).</summary>
public sealed record RewardsOverviewResponse(
    int Pending,
    int Delivered,
    int Expired,
    IReadOnlyList<RewardListItem> Items);
