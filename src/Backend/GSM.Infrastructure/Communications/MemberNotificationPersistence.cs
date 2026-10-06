using GSM.Application.Communications.Abstractions;
using GSM.Domain.Communications;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Communications;

public sealed class MemberNotificationConfiguration : IEntityTypeConfiguration<MemberNotification>
{
    public void Configure(EntityTypeBuilder<MemberNotification> builder)
    {
        builder.ToTable("member_notifications", "communications");

        builder.Property(n => n.Type).HasColumnName("type");
        builder.Property(n => n.Title).HasMaxLength(MemberNotification.TitleMaxLength).IsRequired();
        builder.Property(n => n.Body).HasMaxLength(MemberNotification.BodyMaxLength).IsRequired();
        builder.Property(n => n.Link).HasMaxLength(MemberNotification.LinkMaxLength);

        // Idempotencia de los avisos: un handler o la tarea periódica que se repite no duplica.
        builder.HasIndex(n => new { n.MemberId, n.Type, n.ReferenceId }).IsUnique();
        builder.HasIndex(n => new { n.MemberId, n.ReadAt });
        builder.HasIndex(n => n.CreatedAt);
    }
}

public sealed class MemberNotificationRepository(GsmDbContext dbContext) : IMemberNotificationRepository
{
    private DbSet<MemberNotification> Notifications => dbContext.Set<MemberNotification>();

    public async Task<bool> ExistsAsync(
        Guid memberId, NotificationType type, Guid referenceId, CancellationToken cancellationToken = default)
        => Notifications.Local.Any(n => n.MemberId == memberId && n.Type == type && n.ReferenceId == referenceId)
            || await Notifications.AnyAsync(
                n => n.MemberId == memberId && n.Type == type && n.ReferenceId == referenceId, cancellationToken);

    public void Add(MemberNotification notification) => Notifications.Add(notification);

    public Task<MemberNotification?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => Notifications.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<MemberNotification> Items, int Total)> ListForMemberAsync(
        Guid memberId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = Notifications.AsNoTracking().Where(n => n.MemberId == memberId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(n => n.ReadAt != null)
            .ThenByDescending(n => n.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<int> CountUnreadAsync(Guid memberId, CancellationToken cancellationToken = default)
        => Notifications.CountAsync(n => n.MemberId == memberId && n.ReadAt == null, cancellationToken);

    public Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
        => Notifications.Where(n => n.CreatedAt < cutoff).ExecuteDeleteAsync(cancellationToken);

    public Task MarkAllReadAsync(Guid memberId, DateTimeOffset now, CancellationToken cancellationToken = default)
        => Notifications
            .Where(n => n.MemberId == memberId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.ReadAt, now)
                .SetProperty(n => n.UpdatedAt, now), cancellationToken);
}
