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

    /// <summary>Publicados y vigentes ese día: fijados primero, luego del más reciente al más antiguo.</summary>
    Task<(IReadOnlyList<Announcement> Items, int Total)> ListFeedAsync(
        DateOnly today, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Visibles ese día y publicados después de <paramref name="since"/> (nulo = todos).</summary>
    Task<int> CountVisibleSinceAsync(DateOnly today, DateTimeOffset? since, CancellationToken cancellationToken = default);
}
