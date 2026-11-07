using GSM.Domain.Equipment;
using GSM.Domain.Routines;

namespace GSM.Application.Routines.Abstractions;

/// <summary>
/// Filtro del listado del catálogo. Todo opcional. <c>MuscleGroup</c> es el primer paso del filtro en
/// dos pasos (grupo → músculo); <c>IncludeHidden</c> trae también los ejercicios del sistema que el gym ocultó.
/// <c>OnlyAvailableHere</c> ("solo lo que se puede hacer en mi gym") lo aplica el caso de uso con Equipment, no el repositorio.
/// </summary>
public sealed record ExerciseFilter(
    string? Search,
    Muscle? PrimaryMuscle,
    EquipmentType? Equipment,
    bool IncludeInactive,
    MuscleGroup? MuscleGroup = null,
    ExerciseType? ExerciseType = null,
    Difficulty? Difficulty = null,
    bool IncludeHidden = false,
    bool OnlyAvailableHere = false);

/// <summary>Lo mínimo de un ejercicio que necesitan las respuestas de las rutinas.</summary>
public sealed record ExerciseSummary(
    string Name, Muscle PrimaryMuscle, TrackingType TrackingType, bool IsUnilateral,
    IReadOnlyList<EquipmentType>? Equipment = null,
    IReadOnlyList<Muscle>? SecondaryMuscles = null,
    string? ImageUrl = null,
    string? VideoUrl = null);

/// <summary>
/// Acceso a <see cref="Exercise"/>. **Toda** consulta aplica el filtro híbrido
/// (<c>gym_id IS NULL OR gym_id = @current</c>) — nunca el Global Query Filter.
/// </summary>
public interface IExerciseRepository
{
    Task<Exercise?> GetVisibleByIdAsync(Guid exerciseId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Exercise>> ListVisibleAsync(
        ExerciseFilter filter, CancellationToken cancellationToken = default);

    /// <summary>Los ejercicios visibles de entre <paramref name="exerciseIds"/>, activos o no, ocultos o no.</summary>
    Task<IReadOnlyList<Exercise>> ListVisibleByIdsAsync(
        IReadOnlyCollection<Guid> exerciseIds, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// ¿Algún ejercicio distinto de <paramref name="excludeExerciseId"/> usa esta URL como imagen o video?
    /// Un ejercicio duplicado comparte los archivos del original, así que no se borran mientras alguien los use.
    /// Mira todos los gyms (el archivo es el mismo), por eso no pasa por el filtro de visibilidad.
    /// </summary>
    Task<bool> IsMediaUrlInUseAsync(
        string url, Guid excludeExerciseId, CancellationToken cancellationToken = default);

    void Add(Exercise exercise);
}
