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
    ];
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
}
