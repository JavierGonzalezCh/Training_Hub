using GSM.Application.Equipment.Abstractions;
using GSM.Domain.Equipment;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GSM.Infrastructure.Equipment;

/// <summary>
/// Enums nativos de Postgres del módulo Equipment. <c>EquipmentType</c> vivía en el schema
/// <c>routines</c> con el nombre <c>equipment</c>; la migración E1 lo movió aquí sin tocar sus valores.
/// </summary>
public sealed class EquipmentEnums : IModuleEnums
{
    public IEnumerable<PgEnumMapping> Enums { get; } =
    [
        PgEnumMapping.Of<EquipmentType>("equipment_type", "equipment", LowerCaseNameTranslator.Instance),
        PgEnumMapping.Of<EquipmentStatus>("equipment_status", "equipment", SnakeCaseNameTranslator.Instance),
        PgEnumMapping.Of<EquipmentIssueReason>("equipment_issue_reason", "equipment", SnakeCaseNameTranslator.Instance),
        PgEnumMapping.Of<EquipmentIssueStatus>("equipment_issue_status", "equipment", SnakeCaseNameTranslator.Instance),
        PgEnumMapping.Of<IssueReporterRole>("equipment_issue_reporter_role", "equipment", LowerCaseNameTranslator.Instance),
    ];
}

public sealed class EquipmentIssueConfiguration : IEntityTypeConfiguration<EquipmentIssue>
{
    public void Configure(EntityTypeBuilder<EquipmentIssue> builder)
    {
        builder.ToTable("equipment_issues", "equipment", t =>
            t.HasCheckConstraint(
                "ck_equipment_issues_other_description",
                "reason <> 'other' OR description IS NOT NULL"));

        builder.Property(i => i.Reason).HasColumnName("reason");
        builder.Property(i => i.Status).HasColumnName("status").HasDefaultValue(EquipmentIssueStatus.Open);
        builder.Property(i => i.ReportedByRole).HasColumnName("reported_by_role");
        builder.Property(i => i.Description).HasMaxLength(EquipmentIssue.DescriptionMaxLength);
        builder.Property(i => i.Resolution).HasMaxLength(EquipmentIssue.ResolutionMaxLength);
        builder.Property(i => i.PhotoUrl).HasMaxLength(2048);
        builder.Ignore(i => i.IsUrgent);
        builder.Ignore(i => i.IsClosed);

        // La máquina no se borra (solo se da de baja), así que el reporte siempre conserva su máquina.
        builder.HasOne<EquipmentItem>()
            .WithMany()
            .HasForeignKey(i => i.ItemId)
            .HasConstraintName("fk_equipment_issues_equipment_items")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(i => new { i.GymId, i.Status, i.CreatedAt });
        builder.HasIndex(i => new { i.ItemId, i.Status });

        // Un miembro tiene como máximo un reporte sin cerrar por máquina (Equipment §4).
        builder.HasIndex(i => new { i.ItemId, i.ReportedByUserId })
            .IsUnique()
            .HasDatabaseName("ix_equipment_issues_item_reporter_unclosed")
            .HasFilter("reported_by_role = 'member' AND status IN ('open', 'in_progress')");
        builder.HasIndex(i => i.ReportedByUserId);
    }
}

public sealed class EquipmentModelConfiguration : IEntityTypeConfiguration<EquipmentModel>
{
    public void Configure(EntityTypeBuilder<EquipmentModel> builder)
    {
        builder.ToTable("equipment_models", "equipment");

        builder.Property(m => m.Name).HasMaxLength(EquipmentModel.NameMaxLength).IsRequired();
        builder.Property(m => m.Type).HasColumnName("type");
        builder.Property(m => m.ImageUrl).HasMaxLength(2048);
        builder.Property(m => m.Description).HasMaxLength(EquipmentModel.TextMaxLength);
        builder.Property(m => m.DefaultInstructions).HasMaxLength(EquipmentModel.TextMaxLength);
        builder.Property(m => m.IsActive).HasDefaultValue(true);

        builder.HasIndex(m => m.Name).IsUnique();
        builder.HasIndex(m => m.Type);
    }
}

public sealed class EquipmentItemConfiguration : IEntityTypeConfiguration<EquipmentItem>
{
    public void Configure(EntityTypeBuilder<EquipmentItem> builder)
    {
        builder.ToTable("equipment_items", "equipment", t =>
            t.HasCheckConstraint("ck_equipment_items_quantity", "quantity >= 1"));

        builder.Property(i => i.Name).HasMaxLength(EquipmentModel.NameMaxLength).IsRequired();
        builder.Property(i => i.Type).HasColumnName("type");
        builder.Property(i => i.Status)
            .HasColumnName("status")
            .HasDefaultValue(EquipmentStatus.Available);
        builder.Property(i => i.PhotoUrl).HasMaxLength(2048);
        builder.Property(i => i.Zone).HasMaxLength(EquipmentItem.ZoneMaxLength);
        builder.Property(i => i.Brand).HasMaxLength(EquipmentItem.ShortTextMaxLength);
        builder.Property(i => i.Model).HasMaxLength(EquipmentItem.ShortTextMaxLength);
        builder.Property(i => i.Instructions).HasMaxLength(EquipmentModel.TextMaxLength);
        builder.Property(i => i.Notes).HasMaxLength(EquipmentModel.TextMaxLength);
        builder.Property(i => i.IsActive).HasDefaultValue(true);

        builder.HasIndex(i => new { i.GymId, i.IsActive, i.Type });
        builder.HasIndex(i => new { i.GymId, i.ModelId });
    }
}

public sealed class EquipmentStatusChangeConfiguration : IEntityTypeConfiguration<EquipmentStatusChange>
{
    public void Configure(EntityTypeBuilder<EquipmentStatusChange> builder)
    {
        builder.ToTable("equipment_status_changes", "equipment");

        builder.Property(c => c.FromStatus).HasColumnName("from_status");
        builder.Property(c => c.ToStatus).HasColumnName("to_status");
        builder.Property(c => c.Reason).HasMaxLength(EquipmentStatusChange.ReasonMaxLength);

        builder.HasOne<EquipmentItem>()
            .WithMany()
            .HasForeignKey(c => c.ItemId)
            .HasConstraintName("fk_equipment_status_changes_equipment_items")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => new { c.ItemId, c.ChangedAt });
    }
}

public sealed class EquipmentModelRepository(GsmDbContext dbContext) : IEquipmentModelRepository
{
    private DbSet<EquipmentModel> Models => dbContext.Set<EquipmentModel>();

    public async Task<IReadOnlyList<EquipmentModel>> ListActiveAsync(
        EquipmentType? type, CancellationToken cancellationToken = default)
    {
        var q = Models.AsNoTracking().Where(m => m.IsActive);

        if (type is { } t)
        {
            q = q.Where(m => m.Type == t);
        }

        return await q.OrderBy(m => m.Type).ThenBy(m => m.Name).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EquipmentModel>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await Models.AsNoTracking().Where(m => ids.Contains(m.Id)).ToListAsync(cancellationToken);
    }
}

public sealed class EquipmentItemRepository(GsmDbContext dbContext) : IEquipmentItemRepository
{
    private DbSet<EquipmentItem> Items => dbContext.Set<EquipmentItem>();

    public Task<EquipmentItem?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => Items.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public async Task<IReadOnlyList<EquipmentItem>> ListAsync(
        EquipmentFilter filter, CancellationToken cancellationToken = default)
    {
        var q = Items.AsQueryable();

        if (!filter.IncludeInactive)
        {
            q = q.Where(i => i.IsActive);
        }

        if (filter.Type is { } type)
        {
            q = q.Where(i => i.Type == type);
        }

        if (filter.Status is { } status)
        {
            q = q.Where(i => i.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(filter.Zone))
        {
            var zone = filter.Zone.Trim();
            q = q.Where(i => i.Zone != null && EF.Functions.ILike(i.Zone, zone));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = $"%{filter.Search.Trim()}%";
            q = q.Where(i => EF.Functions.ILike(i.Name, term));
        }

        return await q.OrderBy(i => i.Name).ThenBy(i => i.CreatedAt).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListZonesAsync(CancellationToken cancellationToken = default)
        => await Items
            .Where(i => i.IsActive && i.Zone != null)
            .Select(i => i.Zone!)
            .Distinct()
            .OrderBy(z => z)
            .ToListAsync(cancellationToken);

    public Task<int> CountActiveByModelAsync(Guid modelId, CancellationToken cancellationToken = default)
        => Items.CountAsync(i => i.IsActive && i.ModelId == modelId, cancellationToken);

    public void Add(EquipmentItem item) => Items.Add(item);

    public void AddStatusChange(EquipmentStatusChange change) => dbContext.Set<EquipmentStatusChange>().Add(change);

    public async Task<IReadOnlyList<EquipmentStatusChange>> ListStatusChangesAsync(
        Guid itemId, CancellationToken cancellationToken = default)
        => await dbContext.Set<EquipmentStatusChange>()
            .AsNoTracking()
            .Where(c => c.ItemId == itemId)
            .OrderByDescending(c => c.ChangedAt)
            .ThenByDescending(c => c.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, EquipmentItem>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
        => ids.Count == 0
            ? new Dictionary<Guid, EquipmentItem>()
            : await Items.AsNoTracking().Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken);

    public async Task<IReadOnlyDictionary<EquipmentStatus, int>> CountActiveByStatusAsync(
        CancellationToken cancellationToken = default)
        => await Items
            .Where(i => i.IsActive)
            .GroupBy(i => i.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, cancellationToken);
}

public sealed class EquipmentIssueRepository(GsmDbContext dbContext) : IEquipmentIssueRepository
{
    private DbSet<EquipmentIssue> Issues => dbContext.Set<EquipmentIssue>();

    public void Add(EquipmentIssue issue) => Issues.Add(issue);

    public Task<EquipmentIssue?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => Issues.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public async Task<IReadOnlyList<EquipmentIssue>> ListAsync(
        EquipmentIssueFilter filter, int take, CancellationToken cancellationToken = default)
    {
        var q = Issues.AsNoTracking().AsQueryable();

        if (filter.ItemId is { } itemId)
        {
            q = q.Where(i => i.ItemId == itemId);
        }

        if (filter.Status is { } status)
        {
            q = q.Where(i => i.Status == status);
        }

        if (filter.OpenOnly)
        {
            q = q.Where(i => i.Status == EquipmentIssueStatus.Open || i.Status == EquipmentIssueStatus.InProgress);
        }

        // Urgentes sin cerrar primero, luego los sin cerrar, luego el resto; dentro de cada grupo, lo más reciente.
        return await q
            .OrderByDescending(i => i.Reason == EquipmentIssueReason.Unsafe
                && (i.Status == EquipmentIssueStatus.Open || i.Status == EquipmentIssueStatus.InProgress))
            .ThenByDescending(i => i.Status == EquipmentIssueStatus.Open || i.Status == EquipmentIssueStatus.InProgress)
            .ThenByDescending(i => i.CreatedAt)
            .ThenByDescending(i => i.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EquipmentIssue>> ListByItemAsync(
        Guid itemId, CancellationToken cancellationToken = default)
        => await Issues.Where(i => i.ItemId == itemId).OrderBy(i => i.CreatedAt).ToListAsync(cancellationToken);

    public Task<EquipmentIssue?> FindUnclosedByReporterAsync(
        Guid itemId, Guid userId, CancellationToken cancellationToken = default)
        => Issues.FirstOrDefaultAsync(
            i => i.ItemId == itemId
                 && i.ReportedByUserId == userId
                 && (i.Status == EquipmentIssueStatus.Open || i.Status == EquipmentIssueStatus.InProgress),
            cancellationToken);

    public async Task<IReadOnlyList<EquipmentIssue>> ListByReporterAsync(
        Guid userId, int take, CancellationToken cancellationToken = default)
        => await Issues.AsNoTracking()
            .Where(i => i.ReportedByUserId == userId)
            .OrderByDescending(i => i.CreatedAt)
            .ThenByDescending(i => i.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<(int Open, int Urgent)> CountUnclosedAsync(CancellationToken cancellationToken = default)
    {
        var unclosed = Issues.Where(i => i.Status == EquipmentIssueStatus.Open || i.Status == EquipmentIssueStatus.InProgress);
        var open = await unclosed.CountAsync(cancellationToken);
        var urgent = await unclosed.CountAsync(i => i.Reason == EquipmentIssueReason.Unsafe, cancellationToken);
        return (open, urgent);
    }
}
