using GSM.Domain.Achievements;

namespace GSM.Application.Achievements.Contracts;

public sealed record CreateAchievementRequest(
    string Name,
    string? Description,
    string? IconUrl,
    AchievementConditionType ConditionType,
    int ConditionValue);

public sealed record UpdateAchievementRequest(
    string Name,
    string? Description,
    string? IconUrl,
    AchievementConditionType ConditionType,
    int ConditionValue);

public sealed record AchievementResponse(
    Guid Id,
    string Name,
    string? Description,
    string? IconUrl,
    string ConditionType,
    int ConditionValue,
    bool IsActive,
    DateTimeOffset CreatedAt);

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
    int Progress);
