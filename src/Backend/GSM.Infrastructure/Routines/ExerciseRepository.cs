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

    private DbSet<GymHiddenExercise> HiddenRows => dbContext.Set<GymHiddenExercise>();

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

        if (filter.MuscleGroup is { } group)
        {
            var groupMuscles = MuscleGroups.MusclesOf(group).ToList();
            q = q.Where(e => groupMuscles.Contains(e.PrimaryMuscle));
        }

        if (filter.ExerciseType is { } type)
        {
            q = q.Where(e => e.ExerciseType == type);
        }

        if (filter.Difficulty is { } difficulty)
        {
            q = q.Where(e => e.Difficulty == difficulty);
        }

        if (filter.Equipment is { } equipment)
        {
            q = q.Where(e => e.Equipment.Contains(equipment));
        }

        // Los ocultos del gym no salen salvo que se pida. Solo hay ocultos de ejercicios del sistema.
        if (!filter.IncludeHidden && currentUser.GymId is { } hidingGymId)
        {
            var hidden = HiddenRows.Where(h => h.GymId == hidingGymId).Select(h => h.ExerciseId);
            q = q.Where(e => !hidden.Contains(e.Id));
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

    public async Task<IReadOnlyList<Exercise>> ListVisibleByIdsAsync(
        IReadOnlyCollection<Guid> exerciseIds, CancellationToken cancellationToken = default)
        => exerciseIds.Count == 0
            ? []
            : await VisibleToCurrentGym().Where(e => exerciseIds.Contains(e.Id)).ToListAsync(cancellationToken);

    public Task<bool> NameTakenAsync(
        string name, Guid? gymId, Guid? excludeExerciseId, CancellationToken cancellationToken = default)
        => Exercises.AnyAsync(
            e => e.Name == name
                 && e.GymId == gymId
                 && (excludeExerciseId == null || e.Id != excludeExerciseId),
            cancellationToken);

    public Task<bool> IsMediaUrlInUseAsync(
        string url, Guid excludeExerciseId, CancellationToken cancellationToken = default)
        => Exercises.AnyAsync(
            e => e.Id != excludeExerciseId && (e.ImageUrl == url || e.VideoUrl == url),
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

    public async Task<IReadOnlyDictionary<Guid, ExerciseSummary>> GetVisibleSummariesAsync(
        IReadOnlyCollection<Guid> exerciseIds, CancellationToken cancellationToken = default)
    {
        if (exerciseIds.Count == 0)
        {
            return new Dictionary<Guid, ExerciseSummary>();
        }

        var rows = await VisibleToCurrentGym()
            .Where(e => exerciseIds.Contains(e.Id))
            .Select(e => new { e.Id, e.Name, e.PrimaryMuscle, e.TrackingType, e.IsUnilateral, e.Equipment, e.SecondaryMuscles, e.ImageUrl, e.VideoUrl })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            r => r.Id,
            r => new ExerciseSummary(
                r.Name, r.PrimaryMuscle, r.TrackingType, r.IsUnilateral, r.Equipment, r.SecondaryMuscles, r.ImageUrl, r.VideoUrl));
    }

    public async Task<IReadOnlySet<Guid>> GetHiddenIdsAsync(
        IReadOnlyCollection<Guid> exerciseIds, CancellationToken cancellationToken = default)
    {
        if (currentUser.GymId is not { } gymId || exerciseIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var ids = await HiddenRows
            .Where(h => h.GymId == gymId && exerciseIds.Contains(h.ExerciseId))
            .Select(h => h.ExerciseId)
            .ToListAsync(cancellationToken);

        return ids.ToHashSet();
    }

    public Task<GymHiddenExercise?> GetHiddenAsync(Guid exerciseId, CancellationToken cancellationToken = default)
    {
        var gymId = currentUser.GymId;
        return HiddenRows.FirstOrDefaultAsync(
            h => h.GymId == gymId && h.ExerciseId == exerciseId, cancellationToken);
    }

    public void AddHidden(GymHiddenExercise hidden) => HiddenRows.Add(hidden);

    public void RemoveHidden(GymHiddenExercise hidden) => HiddenRows.Remove(hidden);

    public void Add(Exercise exercise) => Exercises.Add(exercise);
}
