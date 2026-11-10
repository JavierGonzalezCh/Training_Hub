using GSM.Application.Routines.Abstractions;
using GSM.Domain.Routines;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Routines;

public sealed class RoutineTemplateRepository(GsmDbContext dbContext) : IRoutineTemplateRepository
{
    private DbSet<RoutineTemplate> Templates => dbContext.Set<RoutineTemplate>();

    public Task<RoutineTemplate?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => Templates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<RoutineTemplate>> ListAsync(CancellationToken cancellationToken = default)
        => await Templates.OrderBy(t => t.Name).ToListAsync(cancellationToken);

    public async Task<IReadOnlySet<string>> GetBaseKeysAsync(Guid gymId, CancellationToken cancellationToken = default)
        => (await Templates.IgnoreQueryFilters().AsNoTracking()
                .Where(t => t.GymId == gymId && t.BaseKey != null)
                .Select(t => t.BaseKey!)
                .ToListAsync(cancellationToken))
            .ToHashSet();

    public void Add(RoutineTemplate template) => Templates.Add(template);

    public void Remove(RoutineTemplate template) => Templates.Remove(template);
}
