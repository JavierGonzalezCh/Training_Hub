using GSM.Application.Classes.Abstractions;
using GSM.Domain.Classes;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Classes;

/// <summary><see cref="ClassEquipmentItem"/> es <c>ITenantEntity</c>: el Global Query Filter restringe al gym actual (la tarea periódica no tiene gym y ve todos).</summary>
public sealed class ClassEquipmentRepository(GsmDbContext dbContext) : IClassEquipmentRepository
{
    private DbSet<ClassEquipmentItem> Links => dbContext.Set<ClassEquipmentItem>();

    public async Task<IReadOnlyList<ClassEquipmentItem>> ListForClassAsync(
        Guid classId, CancellationToken cancellationToken = default)
        => await Links.Where(l => l.ClassId == classId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ClassEquipmentItem>> ListForClassesAsync(
        IReadOnlyCollection<Guid> classIds, CancellationToken cancellationToken = default)
        => classIds.Count == 0
            ? []
            : await Links.AsNoTracking().Where(l => classIds.Contains(l.ClassId)).ToListAsync(cancellationToken);

    public void Add(ClassEquipmentItem link) => Links.Add(link);

    public void Remove(ClassEquipmentItem link) => Links.Remove(link);
}
