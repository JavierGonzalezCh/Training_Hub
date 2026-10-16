using GSM.Application.Routines.Abstractions;
using GSM.Domain.Routines;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Routines;

/// <summary>
/// <see cref="Routine"/> y sus hijos son <c>ITenantEntity</c>: el Global Query Filter ya restringe
/// al gym actual, así que aquí solo se filtra por miembro / id.
/// </summary>
public sealed class RoutineRepository(GsmDbContext dbContext) : IRoutineRepository
{
    private DbSet<Routine> Routines => dbContext.Set<Routine>();

    private IQueryable<Routine> WithDays => Routines
        .Include(r => r.Days.Where(d => d.RetiredOn == null).OrderBy(d => d.Position))
        .ThenInclude(d => d.Exercises.Where(e => e.RetiredOn == null).OrderBy(e => e.Order));

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
            .Include(r => r.Days.Where(d => d.RetiredOn == null)) // CycleLength sale de los días vigentes
            .Where(r => r.MemberId == memberId)
            .OrderByDescending(r => r.IsActive)
            .ThenByDescending(r => r.StartsOn)
            .ToListAsync(cancellationToken);

    public void Add(Routine routine) => Routines.Add(routine);

    public void TrackNewChildren(Routine routine)
    {
        dbContext.ChangeTracker.DetectChanges();

        // Una fila que ya existía tiene CreatedAt; una recién creada en el dominio, no.
        foreach (var entry in dbContext.ChangeTracker.Entries()
                     .Where(e => e.State == EntityState.Modified
                                 && (e.Entity is RoutineDay or RoutineExercise)
                                 && ((GSM.Domain.Common.BaseEntity)e.Entity).CreatedAt == default)
                     .ToList())
        {
            entry.State = EntityState.Added;
        }
    }

    public async Task ClearNotesForMemberAsync(Guid gymId, Guid memberId, CancellationToken cancellationToken = default)
    {
        await dbContext.Set<Routine>().IgnoreQueryFilters()
            .Where(r => r.GymId == gymId && r.MemberId == memberId && r.Notes != null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Notes, (string?)null), cancellationToken);

        var routineIds = dbContext.Set<Routine>().IgnoreQueryFilters()
            .Where(r => r.GymId == gymId && r.MemberId == memberId)
            .Select(r => r.Id);
        var dayIds = dbContext.Set<RoutineDay>().IgnoreQueryFilters()
            .Where(d => d.GymId == gymId && routineIds.Contains(d.RoutineId))
            .Select(d => d.Id);
        await dbContext.Set<RoutineExercise>().IgnoreQueryFilters()
            .Where(e => e.GymId == gymId && e.Notes != null && dayIds.Contains(e.RoutineDayId))
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Notes, (string?)null), cancellationToken);

        await dbContext.Set<RoutineExerciseLog>().IgnoreQueryFilters()
            .Where(l => l.GymId == gymId && l.MemberId == memberId && l.Notes != null)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.Notes, (string?)null), cancellationToken);
    }
}
