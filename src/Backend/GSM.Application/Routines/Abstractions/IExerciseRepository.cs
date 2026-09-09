using GSM.Domain.Routines;

namespace GSM.Application.Routines.Abstractions;

/// <summary>Filtro del listado del catálogo. Todo opcional.</summary>
public sealed record ExerciseFilter(
    string? Search,
    Muscle? PrimaryMuscle,
    Equipment? Equipment,
    bool IncludeInactive);

/// <summary>
/// Acceso a <see cref="Exercise"/>. **Toda** consulta aplica el filtro híbrido
/// (<c>gym_id IS NULL OR gym_id = @current</c>) — nunca el Global Query Filter.
/// </summary>
public interface IExerciseRepository
{
    Task<Exercise?> GetVisibleByIdAsync(Guid exerciseId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Exercise>> ListVisibleAsync(
        ExerciseFilter filter, CancellationToken cancellationToken = default);

    /// <summary>¿Hay ya un ejercicio con ese nombre en el ámbito indicado (<c>null</c> = catálogo del sistema)?</summary>
    Task<bool> NameTakenAsync(
        string name, Guid? gymId, Guid? excludeExerciseId, CancellationToken cancellationToken = default);

    /// <summary>Todos los <paramref name="exerciseIds"/> que existen y son visibles para el gym actual.</summary>
    Task<IReadOnlySet<Guid>> FilterVisibleIdsAsync(
        IReadOnlyCollection<Guid> exerciseIds, CancellationToken cancellationToken = default);

    void Add(Exercise exercise);
}
