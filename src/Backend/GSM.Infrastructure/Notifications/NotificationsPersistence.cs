using GSM.Application.Notifications.Abstractions;
using GSM.Application.Notifications.Public;
using GSM.Domain.Notifications;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Notifications;

/// <summary>Enums nativos de Postgres del módulo Notifications.</summary>
public sealed class NotificationsEnums : IModuleEnums
{
    public IEnumerable<PgEnumMapping> Enums { get; } =
    [
        PgEnumMapping.Of<NotificationType>("notification_type", "notifications", SnakeCaseNameTranslator.Instance),
        PgEnumMapping.Of<NotificationCategory>("notification_category", "notifications", SnakeCaseNameTranslator.Instance),
        PgEnumMapping.Of<NotificationPriority>("notification_priority", "notifications", LowerCaseNameTranslator.Instance),
        PgEnumMapping.Of<NotificationStatus>("notification_status", "notifications", LowerCaseNameTranslator.Instance),
        PgEnumMapping.Of<DeliveryChannel>("delivery_channel", "notifications", LowerCaseNameTranslator.Instance),
        PgEnumMapping.Of<DeliveryStatus>("delivery_status", "notifications", LowerCaseNameTranslator.Instance),
        PgEnumMapping.Of<PushPlatform>("push_platform", "notifications", SnakeCaseNameTranslator.Instance),
    ];
}

public sealed class PushDeviceConfiguration : IEntityTypeConfiguration<PushDevice>
{
    public void Configure(EntityTypeBuilder<PushDevice> builder)
    {
        builder.ToTable("push_devices", "notifications");

        builder.Property(d => d.Platform).HasColumnName("platform");
        builder.Property(d => d.Token).HasMaxLength(PushDevice.TokenMaxLength).IsRequired();

        // Un token es de un solo dispositivo: registrarlo de nuevo lo reutiliza.
        builder.HasIndex(d => d.Token).IsUnique();
        builder.HasIndex(d => new { d.UserId, d.RevokedAt });
    }
}

public sealed class StaffNotificationPreferenceConfiguration : IEntityTypeConfiguration<StaffNotificationPreference>
{
    public void Configure(EntityTypeBuilder<StaffNotificationPreference> builder)
    {
        builder.ToTable("staff_notification_preferences", "notifications");

        builder.Property(p => p.Type).HasColumnName("type");
        builder.HasIndex(p => new { p.GymId, p.UserId, p.Type }).IsUnique();
    }
}

public sealed class PushDeviceRepository(GsmDbContext dbContext) : IPushDeviceRepository
{
    private DbSet<PushDevice> Devices => dbContext.Set<PushDevice>();

    public Task<PushDevice?> FindByTokenAsync(string token, CancellationToken cancellationToken = default)
        => Devices.FirstOrDefaultAsync(d => d.Token == token, cancellationToken);

    public void Add(PushDevice device) => Devices.Add(device);

    public async Task<IReadOnlyList<PushDevice>> GetActiveForUserAsync(
        Guid userId, IReadOnlyCollection<PushPlatform> platforms, CancellationToken cancellationToken = default)
        => await Devices
            .Where(d => d.UserId == userId && d.RevokedAt == null && platforms.Contains(d.Platform))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, PushDevice>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
        => ids.Count == 0
            ? new Dictionary<Guid, PushDevice>()
            : await Devices.Where(d => ids.Contains(d.Id)).ToDictionaryAsync(d => d.Id, cancellationToken);
}

public sealed class StaffNotificationPreferenceRepository(GsmDbContext dbContext) : IStaffNotificationPreferenceRepository
{
    private DbSet<StaffNotificationPreference> Preferences => dbContext.Set<StaffNotificationPreference>();

    public async Task<IReadOnlyList<StaffNotificationPreference>> GetForUserAsync(
        Guid gymId, Guid userId, CancellationToken cancellationToken = default)
        => await Preferences.Where(p => p.GymId == gymId && p.UserId == userId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetUserIdsWithPushAsync(
        Guid gymId, NotificationType type, CancellationToken cancellationToken = default)
        => await Preferences.AsNoTracking()
            .Where(p => p.GymId == gymId && p.Type == type && p.Push)
            .Select(p => p.UserId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<(Guid UserId, NotificationType Type), bool>> GetForUsersAsync(
        Guid gymId, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
    {
        var rows = await Preferences.AsNoTracking()
            .Where(p => p.GymId == gymId && userIds.Contains(p.UserId))
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(p => (p.UserId, p.Type), p => p.Push);
    }

    public void Add(StaffNotificationPreference preference) => Preferences.Add(preference);
}

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications", "notifications", t =>
            // Un aviso es personal (usuario o miembro, no ambos) o de acción para todo el gym (sin destinatario).
            t.HasCheckConstraint(
                "ck_notifications_recipient",
                "(recipient_user_id IS NULL OR recipient_member_id IS NULL) "
                + "AND (requires_action OR recipient_user_id IS NOT NULL OR recipient_member_id IS NOT NULL)"));

        builder.Property(n => n.Type).HasColumnName("type");
        builder.Property(n => n.Category).HasColumnName("category");
        builder.Property(n => n.Priority).HasColumnName("priority");
        builder.Property(n => n.Status).HasColumnName("status");
        builder.Property(n => n.Title).HasMaxLength(Notification.TitleMaxLength).IsRequired();
        builder.Property(n => n.Body).HasMaxLength(Notification.BodyMaxLength).IsRequired();
        builder.Property(n => n.Link).HasMaxLength(Notification.LinkMaxLength);
        builder.Property(n => n.ImageUrl).HasMaxLength(Notification.ImageUrlMaxLength);

        // Idempotencia: el mismo aviso no sale dos veces aunque el evento se publique dos veces. Los
        // destinatarios nulos (aviso de acción del gym) cuentan como iguales entre sí.
        builder.HasIndex(n => new { n.Type, n.RecipientUserId, n.RecipientMemberId, n.ReferenceId })
            .IsUnique()
            .AreNullsDistinct(false);
        builder.HasIndex(n => new { n.RecipientMemberId, n.ReadAt });
        builder.HasIndex(n => new { n.GymId, n.RequiresAction, n.ResolvedAt });
        builder.HasIndex(n => new { n.Status, n.ScheduledFor });
        builder.HasIndex(n => n.CreatedAt);
    }
}

public sealed class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> builder)
    {
        builder.ToTable("notification_deliveries", "notifications");

        builder.Property(d => d.Channel).HasColumnName("channel");
        builder.Property(d => d.Status).HasColumnName("status");
        builder.Property(d => d.ToAddress).HasMaxLength(320).IsRequired();
        builder.Property(d => d.Subject).HasMaxLength(NotificationDelivery.SubjectMaxLength).IsRequired();
        builder.Property(d => d.LastError).HasMaxLength(500);

        builder.HasOne<Notification>().WithMany().HasForeignKey(d => d.NotificationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(d => new { d.NotificationId, d.Channel });
        builder.HasIndex(d => new { d.Status, d.NextAttemptAt });
    }
}

public sealed class MemberNotificationPreferenceConfiguration : IEntityTypeConfiguration<MemberNotificationPreference>
{
    public void Configure(EntityTypeBuilder<MemberNotificationPreference> builder)
    {
        builder.ToTable("member_notification_preferences", "notifications");

        builder.Property(p => p.Category).HasColumnName("category");
        builder.HasIndex(p => new { p.MemberId, p.Category }).IsUnique();
    }
}

public sealed class NotificationSettingsConfiguration : IEntityTypeConfiguration<NotificationSettings>
{
    public void Configure(EntityTypeBuilder<NotificationSettings> builder)
    {
        builder.ToTable("notification_settings", "notifications");

        builder.Property(s => s.QuietStart).HasColumnType("time without time zone");
        builder.Property(s => s.QuietEnd).HasColumnType("time without time zone");
        builder.HasIndex(s => s.GymId).IsUnique();
    }
}

public sealed class MemberNotificationPreferenceRepository(GsmDbContext dbContext) : IMemberNotificationPreferenceRepository
{
    private DbSet<MemberNotificationPreference> Preferences => dbContext.Set<MemberNotificationPreference>();

    public async Task<IReadOnlyList<MemberNotificationPreference>> GetForMemberAsync(
        Guid memberId, CancellationToken cancellationToken = default)
        => await Preferences.Where(p => p.MemberId == memberId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, MemberNotificationPreference>> GetForMembersAsync(
        IReadOnlyCollection<Guid> memberIds, NotificationCategory category, CancellationToken cancellationToken = default)
    {
        if (memberIds.Count == 0)
        {
            return new Dictionary<Guid, MemberNotificationPreference>();
        }

        var rows = await Preferences.AsNoTracking()
            .Where(p => p.Category == category && memberIds.Contains(p.MemberId))
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(p => p.MemberId);
    }

    public void Add(MemberNotificationPreference preference) => Preferences.Add(preference);
}

public sealed class NotificationSettingsRepository(GsmDbContext dbContext) : INotificationSettingsRepository
{
    private DbSet<NotificationSettings> Settings => dbContext.Set<NotificationSettings>();

    public Task<NotificationSettings?> GetAsync(Guid gymId, CancellationToken cancellationToken = default)
        => Settings.FirstOrDefaultAsync(s => s.GymId == gymId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, NotificationSettings>> GetForGymsAsync(
        IReadOnlyCollection<Guid> gymIds, CancellationToken cancellationToken = default)
    {
        if (gymIds.Count == 0)
        {
            return new Dictionary<Guid, NotificationSettings>();
        }

        var rows = await Settings.AsNoTracking().Where(s => gymIds.Contains(s.GymId)).ToListAsync(cancellationToken);
        return rows.ToDictionary(s => s.GymId);
    }

    public void Add(NotificationSettings settings) => Settings.Add(settings);
}

public sealed class NotificationRepository(GsmDbContext dbContext) : INotificationRepository
{
    private DbSet<Notification> Notifications => dbContext.Set<Notification>();

    private DbSet<NotificationDelivery> Deliveries => dbContext.Set<NotificationDelivery>();

    public void Add(Notification notification) => Notifications.Add(notification);

    public void AddDelivery(NotificationDelivery delivery) => Deliveries.Add(delivery);

    public Task<Notification?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => Notifications.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);

    public Task<Notification?> GetUntrackedAsync(Guid id, CancellationToken cancellationToken = default)
        => Notifications.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, cancellationToken);

    public async Task<Notification?> FindAsync(
        NotificationType type, Guid? userId, Guid? memberId, Guid referenceId,
        CancellationToken cancellationToken = default)
        => Notifications.Local.FirstOrDefault(n => Matches(n, type, userId, memberId, referenceId))
            ?? await Notifications.FirstOrDefaultAsync(
                n => n.Type == type && n.RecipientUserId == userId && n.RecipientMemberId == memberId
                    && n.ReferenceId == referenceId,
                cancellationToken);

    public async Task<bool> ExistsAsync(
        NotificationType type, Guid? userId, Guid? memberId, Guid referenceId,
        CancellationToken cancellationToken = default)
        => Notifications.Local.Any(n => Matches(n, type, userId, memberId, referenceId))
            || await Notifications.AnyAsync(
                n => n.Type == type && n.RecipientUserId == userId && n.RecipientMemberId == memberId
                    && n.ReferenceId == referenceId,
                cancellationToken);

    public async Task<IReadOnlyList<Notification>> ListUnsentByReferenceAsync(
        IReadOnlyCollection<NotificationType> types, Guid referenceId, CancellationToken cancellationToken = default)
        => await Notifications
            .Where(n => types.Contains(n.Type) && n.ReferenceId == referenceId
                && (n.Status == NotificationStatus.Pending || n.Status == NotificationStatus.Held))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlySet<Guid>> GetNotifiedMemberIdsAsync(
        NotificationType type, Guid referenceId, CancellationToken cancellationToken = default)
    {
        var ids = await Notifications.AsNoTracking()
            .Where(n => n.Type == type && n.ReferenceId == referenceId && n.RecipientMemberId != null)
            .Select(n => n.RecipientMemberId!.Value)
            .ToListAsync(cancellationToken);
        var local = Notifications.Local
            .Where(n => n.Type == type && n.ReferenceId == referenceId && n.RecipientMemberId != null)
            .Select(n => n.RecipientMemberId!.Value);
        return ids.Concat(local).ToHashSet();
    }

    // La bandeja muestra lo que ya llegó a su hora y no se canceló; no depende de que el dispatcher haya
    // pasado (el aviso aparece "en cuanto llega su hora", spec §4.2).
    private IQueryable<Notification> Visible(DateTimeOffset now)
        => Notifications.Where(n => n.ScheduledFor <= now && n.Status != NotificationStatus.Cancelled);

    public async Task<(IReadOnlyList<Notification> Items, int Total)> ListForMemberAsync(
        Guid memberId, DateTimeOffset now, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = Visible(now).AsNoTracking()
            .Where(n => n.RecipientMemberId == memberId && n.Type != NotificationType.Announcement && n.Type != NotificationType.ImportantAnnouncement);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(n => n.ReadAt != null)
            .ThenByDescending(n => n.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<int> CountUnreadForMemberAsync(Guid memberId, DateTimeOffset now, CancellationToken cancellationToken = default)
        => Visible(now).CountAsync(
            n => n.RecipientMemberId == memberId && n.Type != NotificationType.Announcement && n.Type != NotificationType.ImportantAnnouncement && n.ReadAt == null,
            cancellationToken);

    public Task MarkAllReadForMemberAsync(Guid memberId, DateTimeOffset now, CancellationToken cancellationToken = default)
        => Notifications
            .Where(n => n.RecipientMemberId == memberId && n.Type != NotificationType.Announcement && n.Type != NotificationType.ImportantAnnouncement
                && n.ReadAt == null && n.ScheduledFor <= now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.ReadAt, now)
                .SetProperty(n => n.UpdatedAt, now), cancellationToken);

    // Staff: los avisos de acción del gym (sin destinatario) y los informativos propios.
    private IQueryable<Notification> ForStaff(Guid userId, DateTimeOffset now)
        => Visible(now).Where(n => (n.RequiresAction && n.RecipientUserId == null && n.RecipientMemberId == null)
            || n.RecipientUserId == userId);

    public async Task<(IReadOnlyList<Notification> Items, int Total)> ListForStaffAsync(
        Guid userId, DateTimeOffset now, bool openOnly, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = ForStaff(userId, now).AsNoTracking();
        if (openOnly)
        {
            query = query.Where(n => n.RequiresAction ? n.ResolvedAt == null : n.ReadAt == null);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(n => (n.RequiresAction ? n.ResolvedAt : n.ReadAt) != null)
            .ThenByDescending(n => n.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<int> CountOpenForStaffAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default)
        => ForStaff(userId, now).CountAsync(
            n => n.RequiresAction ? n.ResolvedAt == null : n.ReadAt == null, cancellationToken);

    public async Task<bool> TryResolveAsync(
        Guid id, Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default)
        => await Notifications
            .Where(n => n.Id == id && n.RequiresAction && n.ResolvedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.ResolvedAt, now)
                .SetProperty(n => n.ResolvedBy, userId)
                .SetProperty(n => n.UpdatedAt, now), cancellationToken) == 1;

    public async Task<IReadOnlyList<Guid>> GetOpenActionIdsAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
        => await Visible(now).AsNoTracking()
            .Where(n => n.RequiresAction && n.ResolvedAt == null)
            .Select(n => n.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Notification>> GetDueAsync(
        DateTimeOffset now, int take, CancellationToken cancellationToken = default)
        => await Notifications
            .Where(n => (n.Status == NotificationStatus.Pending && n.ScheduledFor <= now)
                || (n.Status == NotificationStatus.Held && n.ReleaseAt <= now))
            .OrderBy(n => n.ScheduledFor)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<NotificationDelivery>> GetDeliveriesAsync(
        IReadOnlyCollection<Guid> notificationIds, CancellationToken cancellationToken = default)
        => await Deliveries.Where(d => notificationIds.Contains(d.NotificationId)).ToListAsync(cancellationToken);

    public Task<int> PurgeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
        => Notifications
            .Where(n => n.CreatedAt < cutoff
                && (n.RecipientMemberId != null || n.ReadAt != null || n.ResolvedAt != null))
            .ExecuteDeleteAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, AnnouncementEmailDelivery>> GetEmailDeliveryAsync(
        NotificationType type, IReadOnlyCollection<Guid> referenceIds, CancellationToken cancellationToken = default)
    {
        if (referenceIds.Count == 0)
        {
            return new Dictionary<Guid, AnnouncementEmailDelivery>();
        }

        var rows = await (
                from d in Deliveries.AsNoTracking()
                join n in Notifications.AsNoTracking() on d.NotificationId equals n.Id
                where d.Channel == DeliveryChannel.Email && (n.Type == type || (type == NotificationType.Announcement && n.Type == NotificationType.ImportantAnnouncement)) && referenceIds.Contains(n.ReferenceId)
                group d by new { n.ReferenceId, d.Status }
                into g
                select new { g.Key.ReferenceId, g.Key.Status, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return rows.GroupBy(r => r.ReferenceId).ToDictionary(
            g => g.Key,
            g => new AnnouncementEmailDelivery(
                g.Where(r => r.Status == DeliveryStatus.Pending).Sum(r => r.Count),
                g.Where(r => r.Status == DeliveryStatus.Sent).Sum(r => r.Count),
                g.Where(r => r.Status == DeliveryStatus.Failed).Sum(r => r.Count)));
    }

    private static bool Matches(Notification n, NotificationType type, Guid? userId, Guid? memberId, Guid referenceId)
        => n.Type == type && n.RecipientUserId == userId && n.RecipientMemberId == memberId
            && n.ReferenceId == referenceId;
}
