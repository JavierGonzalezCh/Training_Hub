using GSM.Application.Communications.Contracts;
using GSM.Domain.Communications;

namespace GSM.Application.Communications.Abstractions;

public interface IAnnouncementReactionRepository
{
    Task<AnnouncementReaction?> GetAsync(Guid announcementId, Guid memberId, CancellationToken cancellationToken = default);

    void Add(AnnouncementReaction reaction);

    void Remove(AnnouncementReaction reaction);

    /// <summary>Totales agregados en la base (sin cargar las reacciones en memoria).</summary>
    Task<IReadOnlyDictionary<Guid, ReactionCounts>> CountAsync(
        IReadOnlyCollection<Guid> announcementIds, CancellationToken cancellationToken = default);

    /// <summary>La reacción del miembro en cada anuncio que tenga una.</summary>
    Task<IReadOnlyDictionary<Guid, ReactionKind>> GetOwnAsync(
        Guid memberId, IReadOnlyCollection<Guid> announcementIds, CancellationToken cancellationToken = default);
}
