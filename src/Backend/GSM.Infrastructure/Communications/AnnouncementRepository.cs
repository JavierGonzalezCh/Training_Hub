using GSM.Application.Communications.Abstractions;
using GSM.Domain.Communications;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Communications;

/// <summary>Todas las consultas pasan por el Global Query Filter: solo ven el gym actual.</summary>
public sealed class AnnouncementRepository(GsmDbContext dbContext) : IAnnouncementRepository
{
    private DbSet<Announcement> Announcements => dbContext.Set<Announcement>();

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
        DateOnly today, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = Announcements.AsNoTracking()
            .Where(a => a.Status == AnnouncementStatus.Published && (a.ExpiresOn == null || a.ExpiresOn >= today));

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(a => a.Pinned)
            .ThenByDescending(a => a.PublishedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<int> CountVisibleSinceAsync(
        DateOnly today, DateTimeOffset? since, CancellationToken cancellationToken = default)
        => Announcements.CountAsync(
            a => a.Status == AnnouncementStatus.Published
                && (a.ExpiresOn == null || a.ExpiresOn >= today)
                && (since == null || a.PublishedAt > since),
            cancellationToken);
}
