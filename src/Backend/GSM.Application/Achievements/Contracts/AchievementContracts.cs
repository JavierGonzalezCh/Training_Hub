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
