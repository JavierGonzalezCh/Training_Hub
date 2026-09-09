using GSM.Application.Common.Abstractions;
using GSM.Application.Routines.Abstractions;
using GSM.Domain.Routines;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Routines;

/// <summary>
/// <see cref="Exercise"/> queda fuera del Global Query Filter. El aislamiento entre gyms lo
/// garantiza <see cref="VisibleToCurrentGym"/>: <c>gym_id IS NULL OR gym_id = @current</c>,
/// y **toda** consulta de lectura pasa por él.
/// </summary>
public sealed class ExerciseRepository(GsmDbContext dbContext, ICurrentUserService currentUser)
    : IExerciseRepository
{
    private DbSet<Exercise> Exercises => dbContext.Set<Exercise>();

    private IQueryable<Exercise> VisibleToCurrentGym()
    {
        var gymId = currentUser.GymId;
        return Exercises.Where(e => e.GymId == null || e.GymId == gymId);
    }

    public Task<Exercise?> GetVisibleByIdAsync(Guid exerciseId, CancellationToken cancellationToken = default)
        => VisibleToCurrentGym().FirstOrDefaultAsync(e => e.Id == exerciseId, cancellationToken);

    public async Task<IReadOnlyList<Exercise>> ListVisibleAsync(
        ExerciseFilter filter, CancellationToken cancellationToken = default)
    {
        var q = VisibleToCurrentGym();

        if (!filter.IncludeInactive)
        {
            q = q.Where(e => e.IsActive);
        }

        if (filter.PrimaryMuscle is { } muscle)
        {
            q = q.Where(e => e.PrimaryMuscle == muscle);
        }

        if (filter.Equipment is { } equipment)
        {
            q = q.Where(e => e.Equipment.Contains(equipment));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = $"%{filter.Search.Trim()}%";
            q = q.Where(e => EF.Functions.ILike(e.Name, term));
        }

        return await q
            .OrderBy(e => e.GymId == null ? 0 : 1)
            .ThenBy(e => e.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> NameTakenAsync(
        string name, Guid? gymId, Guid? excludeExerciseId, CancellationToken cancellationToken = default)
        => Exercises.AnyAsync(
            e => e.Name == name
                 && e.GymId == gymId
                 && (excludeExerciseId == null || e.Id != excludeExerciseId),
            cancellationToken);

    public async Task<IReadOnlySet<Guid>> FilterVisibleIdsAsync(
        IReadOnlyCollection<Guid> exerciseIds, CancellationToken cancellationToken = default)
    {
        if (exerciseIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var found = await VisibleToCurrentGym()
            .Where(e => exerciseIds.Contains(e.Id))
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);

        return found.ToHashSet();
    }

    public void Add(Exercise exercise) => Exercises.Add(exercise);
}
