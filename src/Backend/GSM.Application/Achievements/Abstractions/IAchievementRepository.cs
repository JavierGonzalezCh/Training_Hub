using GSM.Domain.Achievements;

namespace GSM.Application.Achievements.Abstractions;

public interface IAchievementRepository
{
    Task<Achievement?> GetByIdAsync(Guid achievementId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Achievement>> ListAsync(
        bool includeInactive, CancellationToken cancellationToken = default);

    Task<bool> NameExistsAsync(
        string name, Guid? excludeAchievementId, CancellationToken cancellationToken = default);

    void Add(Achievement achievement);

    /// <summary>Logros activos que el miembro **todavía no** ha desbloqueado.</summary>
    Task<IReadOnlyList<Achievement>> ListPendingForMemberAsync(
        Guid memberId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, DateTimeOffset>> GetUnlockedForMemberAsync(
        Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>Miembros que ya tienen desbloqueado ese logro (para no repetirlo en el otorgamiento retroactivo).</summary>
    Task<IReadOnlySet<Guid>> GetMemberIdsWithUnlockAsync(
        Guid achievementId, CancellationToken cancellationToken = default);

    void AddUnlock(MemberAchievement unlock);

    /// <summary>La fila de desbloqueo del miembro para ese logro (incluida una retirada). Nulo si nunca lo tuvo.</summary>
    Task<MemberAchievement?> GetUnlockAsync(
        Guid memberId, Guid achievementId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StreakFreeze>> ListStreakFreezesAsync(
        Guid memberId, CancellationToken cancellationToken = default);

    void AddStreakFreeze(StreakFreeze freeze);
}
