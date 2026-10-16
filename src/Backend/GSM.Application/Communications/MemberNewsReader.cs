using GSM.Application.Common.Abstractions;
using GSM.Application.Communications.Abstractions;
using GSM.Application.Communications.Public;

namespace GSM.Application.Communications;

/// <summary>Novedades del miembro para su inicio (D2). Solo lectura; reutiliza las consultas del feed.</summary>
public sealed class MemberNewsReader(
    IClock clock,
    IAnnouncementRepository announcements,
    IMemberFeedStateRepository feedStates) : IMemberNewsReader
{
    public async Task<MemberNewsView> GetAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        var today = clock.Today;

        // El feed ya viene con los fijados primero y luego del más reciente al más antiguo.
        var (items, _) = await announcements.ListFeedAsync(memberId, today, 1, 1, cancellationToken);
        var first = items.FirstOrDefault();

        var state = await feedStates.GetAsync(memberId, cancellationToken);
        var unread = await announcements.CountVisibleSinceAsync(memberId, today, state?.AnnouncementsSeenAt, cancellationToken);

        return new MemberNewsView(
            first is null || first.PublishedAt is null
                ? null
                : new FeaturedAnnouncementView(first.Id, first.Title, first.Pinned, first.IsImportant, first.PublishedAt.Value),
            unread);
    }
}
