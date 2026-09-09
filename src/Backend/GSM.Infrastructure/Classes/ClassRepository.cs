using GSM.Application.Classes.Abstractions;
using GSM.Domain.Classes;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Classes;

/// <summary>
/// <see cref="GymClass"/> es <c>ITenantEntity</c>: el Global Query Filter ya restringe al gym
/// actual, así que aquí solo se filtra por id / estado.
/// </summary>
public sealed class ClassRepository(GsmDbContext dbContext) : IClassRepository
{
    private DbSet<GymClass> Classes => dbContext.Set<GymClass>();

    public Task<GymClass?> GetAsync(Guid classId, CancellationToken cancellationToken = default)
        => Classes.FirstOrDefaultAsync(c => c.Id == classId, cancellationToken);

    public async Task<IReadOnlyList<GymClass>> ListAsync(
        ClassFilter filter, CancellationToken cancellationToken = default)
    {
        var q = Classes.AsQueryable();

        if (!filter.IncludeInactive)
        {
            q = q.Where(c => c.IsActive);
        }

        return await q
            .OrderBy(c => c.StartTime)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);
    }

    public void Add(GymClass gymClass) => Classes.Add(gymClass);
}
