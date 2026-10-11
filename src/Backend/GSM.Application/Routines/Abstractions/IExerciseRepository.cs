using GSM.Domain.Equipment;
using GSM.Domain.Routines;

namespace GSM.Application.Routines.Abstractions;

/// <summary>
/// Filtro del listado del catálogo. Todo opcional. <c>MuscleGroup</c> es el primer paso del filtro en
/// dos pasos (grupo → músculo); <c>IncludeHidden</c> trae también los ejercicios del sistema que el gym ocultó.
/// </summary>
public sealed record ExerciseFilter(
    string? Search,
    Muscle? PrimaryMuscle,
    EquipmentType? Equipment,
    bool IncludeInactive,
    MuscleGroup? MuscleGroup = null,
    ExerciseType? ExerciseType = null,
    Difficulty? Difficulty = null,
    bool IncludeHidden = false);

/// <summary>Lo mínimo de un ejercicio que necesitan las respuestas de las rutinas.</summary>
public sealed record ExerciseSummary(
    string Name, Muscle PrimaryMuscle, TrackingType TrackingType, bool IsUnilateral);

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

    /// <summary>
    /// Nombre, músculo principal y medición de cada ejercicio visible pedido; los no visibles no aparecen.
    /// Incluye los ocultos: ocultar no cambia las rutinas que ya usan el ejercicio.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, ExerciseSummary>> GetVisibleSummariesAsync(
        IReadOnlyCollection<Guid> exerciseIds, CancellationToken cancellationToken = default);

    /// <summary>De los <paramref name="exerciseIds"/>, los que el gym actual tiene ocultos.</summary>
    Task<IReadOnlySet<Guid>> GetHiddenIdsAsync(
        IReadOnlyCollection<Guid> exerciseIds, CancellationToken cancellationToken = default);

    /// <summary>La marca de "oculto" del gym actual sobre un ejercicio, o <c>null</c>.</summary>
    Task<GymHiddenExercise?> GetHiddenAsync(Guid exerciseId, CancellationToken cancellationToken = default);

    void AddHidden(GymHiddenExercise hidden);

    void RemoveHidden(GymHiddenExercise hidden);

    void Add(Exercise exercise);
}
