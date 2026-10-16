using GSM.Domain.Achievements;

namespace GSM.Application.Achievements.Abstractions;

public interface IRankingRepository
{
    /// <summary>Todos los participantes del gym en contexto.</summary>
    Task<IReadOnlyList<RankingParticipant>> ListAsync(CancellationToken cancellationToken = default);

    Task<RankingParticipant?> GetByMemberAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>¿Otro participante del gym ya usa ese alias (sin distinguir mayúsculas)?</summary>
    Task<bool> AliasTakenAsync(string alias, Guid exceptMemberId, CancellationToken cancellationToken = default);

    void Add(RankingParticipant participant);

    void Remove(RankingParticipant participant);
}
