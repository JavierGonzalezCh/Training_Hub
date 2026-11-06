using GSM.Application.Routines.Abstractions;
using GSM.Domain.Routines;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Routines;

public sealed class RoutineDayOverrideRepository(GsmDbContext dbContext) : IRoutineDayOverrideRepository
{
    private DbSet<RoutineDayOverride> Overrides => dbContext.Set<RoutineDayOverride>();

    public async Task<IReadOnlyList<RoutineDayOverride>> ListAsync(
        Guid memberId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        => await Overrides
            .Where(o => o.MemberId == memberId && o.Date >= from && o.Date <= to)
            .OrderBy(o => o.Date)
            .ToListAsync(cancellationToken);

    public void Add(RoutineDayOverride change) => Overrides.Add(change);

    public void Remove(RoutineDayOverride change) => Overrides.Remove(change);
}
