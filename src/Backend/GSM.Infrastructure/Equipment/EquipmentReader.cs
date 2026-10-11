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
}
