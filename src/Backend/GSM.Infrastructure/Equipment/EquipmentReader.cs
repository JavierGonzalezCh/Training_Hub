using GSM.Application.Equipment.Public;
using GSM.Domain.Equipment;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Equipment;

public sealed class EquipmentReader(GsmDbContext dbContext) : IEquipmentReader
{
    public async Task<GymEquipmentAvailability> GetAvailabilityAsync(
        Guid gymId, CancellationToken cancellationToken = default)
    {
        // El gym viene explícito: el lector se usa también fuera de una petición del propio gym.
        var rows = await dbContext.Set<EquipmentItem>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(i => i.GymId == gymId && i.IsActive)
            .Select(i => new { i.Type, i.Status })
            .ToListAsync(cancellationToken);

        var available = rows
            .Where(r => r.Status == EquipmentStatus.Available)
            .Select(r => r.Type)
            .ToHashSet();
        available.Add(EquipmentType.Bodyweight);

        return new GymEquipmentAvailability(rows.Count > 0, available);
    }

    public async Task<IReadOnlyList<EquipmentItemInfo>> GetActiveItemsAsync(
        Guid gymId, CancellationToken cancellationToken = default)
    {
        var items = await dbContext.Set<EquipmentItem>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(i => i.GymId == gymId && i.IsActive)
            .OrderBy(i => i.Name)
            .ToListAsync(cancellationToken);

        var modelIds = items.Where(i => i.ModelId != null).Select(i => i.ModelId!.Value).Distinct().ToList();
        var modelImages = modelIds.Count == 0
            ? new Dictionary<Guid, string?>()
            : await dbContext.Set<EquipmentModel>()
                .AsNoTracking()
                .Where(m => modelIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, m => m.ImageUrl, cancellationToken);

        return items
            .Select(i => new EquipmentItemInfo(
                i.Id,
                i.ModelId,
                i.Name,
                i.Type,
                i.PhotoUrl ?? (i.ModelId is { } mid && modelImages.TryGetValue(mid, out var img) ? img : null),
                i.Zone,
                i.Instructions,
                i.Status,
                i.ExpectedBackOn,
                i.Quantity))
            .ToList();
    }

    public async Task<IReadOnlyList<EquipmentModelInfo>> GetModelsAsync(
        IReadOnlyCollection<Guid> modelIds, CancellationToken cancellationToken = default)
    {
        if (modelIds.Count == 0)
        {
            return [];
        }

        return await dbContext.Set<EquipmentModel>()
            .AsNoTracking()
            .Where(m => modelIds.Contains(m.Id))
            .Select(m => new EquipmentModelInfo(m.Id, m.Name, m.Type))
            .ToListAsync(cancellationToken);
    }
}
