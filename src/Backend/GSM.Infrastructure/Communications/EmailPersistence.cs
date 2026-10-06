using GSM.Application.Communications.Abstractions;
using GSM.Application.Communications.Contracts;
using GSM.Domain.Communications;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Communications;

public sealed class EmailOutboxItemConfiguration : IEntityTypeConfiguration<EmailOutboxItem>
{
    public void Configure(EntityTypeBuilder<EmailOutboxItem> builder)
    {
        builder.ToTable("email_outbox", "communications");

        builder.Property(e => e.Kind).HasColumnName("kind");
        builder.Property(e => e.Status).HasColumnName("status");
        builder.Property(e => e.ToAddress).HasMaxLength(320).IsRequired();
        builder.Property(e => e.Subject).HasMaxLength(EmailOutboxItem.SubjectMaxLength).IsRequired();
        builder.Property(e => e.LastError).HasMaxLength(500);

        // El mismo aviso no se envía dos veces aunque se encole dos veces.
        builder.HasIndex(e => new { e.MemberId, e.Kind, e.ReferenceId }).IsUnique();
        builder.HasIndex(e => new { e.Status, e.CreatedAt });
        builder.HasIndex(e => new { e.Kind, e.ReferenceId });
    }
}

public sealed class MemberCommunicationPreferencesConfiguration : IEntityTypeConfiguration<MemberCommunicationPreferences>
{
    public void Configure(EntityTypeBuilder<MemberCommunicationPreferences> builder)
    {
        builder.ToTable("member_communication_preferences", "communications");
        builder.HasIndex(p => p.MemberId).IsUnique();
    }
}

public sealed class CommunicationsSettingsConfiguration : IEntityTypeConfiguration<CommunicationsSettings>
{
    public void Configure(EntityTypeBuilder<CommunicationsSettings> builder)
    {
        builder.ToTable("settings", "communications");
        builder.HasIndex(s => s.GymId).IsUnique();
    }
}

public sealed class EmailOutboxRepository(GsmDbContext dbContext) : IEmailOutboxRepository
{
    private DbSet<EmailOutboxItem> Outbox => dbContext.Set<EmailOutboxItem>();

    public async Task<bool> ExistsAsync(
        Guid memberId, EmailKind kind, Guid referenceId, CancellationToken cancellationToken = default)
        => Outbox.Local.Any(e => e.MemberId == memberId && e.Kind == kind && e.ReferenceId == referenceId)
            || await Outbox.AnyAsync(
                e => e.MemberId == memberId && e.Kind == kind && e.ReferenceId == referenceId, cancellationToken);

    public void Add(EmailOutboxItem item) => Outbox.Add(item);

    public async Task<IReadOnlyList<EmailOutboxItem>> GetPendingBatchAsync(
        int size, CancellationToken cancellationToken = default)
        => await Outbox
            .Where(e => e.Status == EmailStatus.Pending)
            .OrderBy(e => e.CreatedAt)
            .Take(size)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, EmailDelivery>> GetDeliveryAsync(
        IReadOnlyCollection<Guid> announcementIds, CancellationToken cancellationToken = default)
    {
        if (announcementIds.Count == 0)
        {
            return new Dictionary<Guid, EmailDelivery>();
        }

        var rows = await Outbox.AsNoTracking()
            .Where(e => e.Kind == EmailKind.Announcement && announcementIds.Contains(e.ReferenceId))
            .GroupBy(e => new { e.ReferenceId, e.Status })
            .Select(g => new { g.Key.ReferenceId, g.Key.Status, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return rows.GroupBy(r => r.ReferenceId).ToDictionary(
            g => g.Key,
            g => new EmailDelivery(
                g.Where(r => r.Status == EmailStatus.Pending).Sum(r => r.Count),
                g.Where(r => r.Status == EmailStatus.Sent).Sum(r => r.Count),
                g.Where(r => r.Status == EmailStatus.Failed).Sum(r => r.Count)));
    }
}

public sealed class MemberCommunicationPreferencesRepository(GsmDbContext dbContext)
    : IMemberCommunicationPreferencesRepository
{
    private DbSet<MemberCommunicationPreferences> Preferences => dbContext.Set<MemberCommunicationPreferences>();

    public Task<MemberCommunicationPreferences?> GetAsync(Guid memberId, CancellationToken cancellationToken = default)
        => Preferences.FirstOrDefaultAsync(p => p.MemberId == memberId, cancellationToken);

    public void Add(MemberCommunicationPreferences preferences) => Preferences.Add(preferences);

    public async Task<IReadOnlySet<Guid>> GetOptedOutAsync(
        IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken = default)
    {
        if (memberIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var ids = await Preferences.AsNoTracking()
            .Where(p => p.EmailOptOut && memberIds.Contains(p.MemberId))
            .Select(p => p.MemberId)
            .ToListAsync(cancellationToken);
        return ids.ToHashSet();
    }
}

public sealed class CommunicationsSettingsRepository(GsmDbContext dbContext) : ICommunicationsSettingsRepository
{
    private DbSet<CommunicationsSettings> Settings => dbContext.Set<CommunicationsSettings>();

    public Task<CommunicationsSettings?> GetAsync(Guid gymId, CancellationToken cancellationToken = default)
        => Settings.FirstOrDefaultAsync(s => s.GymId == gymId, cancellationToken);

    public void Add(CommunicationsSettings settings) => Settings.Add(settings);
}
