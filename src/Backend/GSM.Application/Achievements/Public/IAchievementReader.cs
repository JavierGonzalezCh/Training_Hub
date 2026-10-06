using GSM.Domain.Achievements;

namespace GSM.Application.Achievements.Public;

public interface IAchievementReader
{
    /// <summary>Todos los logros activos del gym con el estado del miembro (desbloqueado + progreso).</summary>
    Task<IReadOnlyList<MemberAchievementView>> GetForMemberAsync(
        Guid memberId, Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>Nombre del logro (para los avisos de Comunicaciones). Nulo si no existe.</summary>
    Task<string?> GetAchievementNameAsync(Guid achievementId, CancellationToken cancellationToken = default);
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
    int Progress);
