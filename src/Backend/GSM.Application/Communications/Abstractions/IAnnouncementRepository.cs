using GSM.Domain.Communications;

namespace GSM.Application.Communications.Abstractions;

public interface IAnnouncementRepository
{
    Task<Announcement?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(Announcement announcement);

    /// <summary>Fijados del gym actual, sin contar <paramref name="excludingId"/>.</summary>
    Task<int> CountPinnedAsync(Guid? excludingId, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Announcement> Items, int Total)> ListAsync(
        AnnouncementStatus? status, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publicados y vigentes ese día que <paramref name="memberId"/> puede ver (los de "todos" y los
    /// segmentados que lo incluyen): fijados primero, luego del más reciente al más antiguo.
    /// </summary>
    Task<(IReadOnlyList<Announcement> Items, int Total)> ListFeedAsync(
        Guid memberId, DateOnly today, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Visibles ese día y publicados después de <paramref name="since"/> (nulo = todos).</summary>
    Task<int> CountVisibleSinceAsync(Guid memberId, DateOnly today, DateTimeOffset? since, CancellationToken cancellationToken = default);

    /// <summary>¿Es <paramref name="memberId"/> destinatario de este anuncio? Los de "todos" lo ven todos.</summary>
    Task<bool> IsVisibleToMemberAsync(Announcement announcement, Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// CO4: anuncios programados cuya hora ya llegó, de <b>todos</b> los gyms (la tarea corre sin usuario; el
    /// filtro global se ignora de forma explícita), del más antiguo al más reciente. Con seguimiento de cambios.
    /// </summary>
    Task<IReadOnlyList<Announcement>> ListDueScheduledAsync(
        DateTimeOffset now, int take, CancellationToken cancellationToken = default);

    /// <summary>Fija los destinatarios de un anuncio segmentado (se guardan con el siguiente SaveChanges).</summary>
    void AddRecipients(IEnumerable<AnnouncementRecipient> recipients);

    /// <summary>Ids de los destinatarios fijados de un anuncio segmentado.</summary>
    Task<IReadOnlyList<Guid>> GetRecipientIdsAsync(Guid announcementId, CancellationToken cancellationToken = default);
}
