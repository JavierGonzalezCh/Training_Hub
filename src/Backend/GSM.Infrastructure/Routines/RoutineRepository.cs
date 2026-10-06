using GSM.Application.Routines.Abstractions;
using GSM.Domain.Routines;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Routines;

/// <summary>
/// <see cref="Routine"/> y sus hijos son <c>ITenantEntity</c>: el Global Query Filter ya restringe
/// al gym actual, así que aquí solo se filtra por socio / id.
/// </summary>
public sealed class RoutineRepository(GsmDbContext dbContext) : IRoutineRepository
{
    private DbSet<Routine> Routines => dbContext.Set<Routine>();

    private IQueryable<Routine> WithDays => Routines
        .Include(r => r.Days.OrderBy(d => d.Position))
        .ThenInclude(d => d.Exercises.OrderBy(e => e.Order));

    public Task<Routine?> GetWithDaysAsync(Guid routineId, CancellationToken cancellationToken = default)
        => WithDays.AsSplitQuery().FirstOrDefaultAsync(r => r.Id == routineId, cancellationToken);

    public Task<Routine?> GetActiveForMemberWithDaysAsync(
        Guid memberId, CancellationToken cancellationToken = default)
        => WithDays.AsSplitQuery()
            .OrderByDescending(r => r.StartsOn)
            .FirstOrDefaultAsync(r => r.MemberId == memberId && r.IsActive, cancellationToken);

    public async Task<IReadOnlyList<Routine>> GetActiveForMemberAsync(
        Guid memberId, CancellationToken cancellationToken = default)
        => await Routines
            .Where(r => r.MemberId == memberId && r.IsActive)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Routine>> ListForMemberAsync(
        Guid memberId, CancellationToken cancellationToken = default)
        => await Routines
            .Include(r => r.Days) // CycleLength sale de los días
            .Where(r => r.MemberId == memberId)
            .OrderByDescending(r => r.IsActive)
            .ThenByDescending(r => r.StartsOn)
            .ToListAsync(cancellationToken);

    public void Add(Routine routine) => Routines.Add(routine);

    public void RemoveDaysOf(Routine routine)
    {
        dbContext.RemoveRange(routine.Days.SelectMany(d => d.Exercises).ToList());
        dbContext.RemoveRange(routine.Days.ToList());
    }
}
