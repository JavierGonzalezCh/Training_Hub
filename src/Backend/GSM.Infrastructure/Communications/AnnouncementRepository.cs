using GSM.Application.Communications.Abstractions;
using GSM.Domain.Communications;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Communications;

/// <summary>Todas las consultas pasan por el Global Query Filter: solo ven el gym actual.</summary>
public sealed class AnnouncementRepository(GsmDbContext dbContext) : IAnnouncementRepository
{
    private DbSet<Announcement> Announcements => dbContext.Set<Announcement>();

    private DbSet<AnnouncementRecipient> Recipients => dbContext.Set<AnnouncementRecipient>();

    public Task<Announcement?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => Announcements.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public void Add(Announcement announcement) => Announcements.Add(announcement);

    public Task<int> CountPinnedAsync(Guid? excludingId, CancellationToken cancellationToken = default)
        => Announcements.CountAsync(
            a => a.Pinned && a.Status != AnnouncementStatus.Archived && (excludingId == null || a.Id != excludingId),
            cancellationToken);

    public async Task<(IReadOnlyList<Announcement> Items, int Total)> ListAsync(
        AnnouncementStatus? status, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = Announcements.AsNoTracking();
        if (status is { } s)
        {
            query = query.Where(a => a.Status == s);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(a => a.Pinned)
            .ThenByDescending(a => a.PublishedAt ?? a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<(IReadOnlyList<Announcement> Items, int Total)> ListFeedAsync(
        Guid memberId, DateOnly today, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = VisibleTo(memberId)
            .Where(a => a.Status == AnnouncementStatus.Published && (a.ExpiresOn == null || a.ExpiresOn >= today));

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(a => a.IsImportant)
            .ThenByDescending(a => a.Pinned)
            .ThenByDescending(a => a.PublishedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<int> CountVisibleSinceAsync(
        Guid memberId, DateOnly today, DateTimeOffset? since, CancellationToken cancellationToken = default)
        => VisibleTo(memberId).CountAsync(
            a => a.Status == AnnouncementStatus.Published
                && (a.ExpiresOn == null || a.ExpiresOn >= today)
                && (since == null || a.PublishedAt > since),
            cancellationToken);

    public async Task<bool> IsVisibleToMemberAsync(
        Announcement announcement, Guid memberId, CancellationToken cancellationToken = default)
        => !announcement.IsSegmented
            || await Recipients.AnyAsync(
                r => r.AnnouncementId == announcement.Id && r.MemberId == memberId, cancellationToken);

    public void AddRecipients(IEnumerable<AnnouncementRecipient> recipients) => Recipients.AddRange(recipients);

    public async Task<IReadOnlyList<Guid>> GetRecipientIdsAsync(
        Guid announcementId, CancellationToken cancellationToken = default)
        => await Recipients.AsNoTracking()
            .Where(r => r.AnnouncementId == announcementId)
            .Select(r => r.MemberId)
            .ToListAsync(cancellationToken);

    /// <summary>Anuncios de "todos" más los segmentados donde el miembro es destinatario.</summary>
    private IQueryable<Announcement> VisibleTo(Guid memberId)
        => Announcements.AsNoTracking().Where(a => !a.IsSegmented
            || Recipients.Any(r => r.AnnouncementId == a.Id && r.MemberId == memberId));
}
