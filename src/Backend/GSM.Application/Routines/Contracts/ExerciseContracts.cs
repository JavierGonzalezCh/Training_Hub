using GSM.Domain.Equipment;
using GSM.Domain.Routines;

namespace GSM.Application.Routines.Contracts;

/// <summary>
/// <c>TrackingType</c> nulo = el sugerido por el tipo (<c>cardio</c> → tiempo y distancia; <c>mobility</c> y
/// <c>stretching</c> → tiempo; el resto, repeticiones y peso). Sin <c>ExerciseType</c> es <c>strength</c>; sin <c>IsUnilateral</c>, falso; sin
/// <c>Difficulty</c>, <c>intermediate</c>.
/// </summary>
public sealed record CreateExerciseRequest(
    string Name,
    string? Description,
    Muscle PrimaryMuscle,
    IReadOnlyList<Muscle>? SecondaryMuscles,
    IReadOnlyList<EquipmentType>? Equipment,
    string? VideoUrl,
    string? ImageUrl,
    ExerciseType? ExerciseType = null,
    TrackingType? TrackingType = null,
    bool? IsUnilateral = null,
    Difficulty? Difficulty = null);

public sealed record UpdateExerciseRequest(
    string Name,
    string? Description,
    Muscle PrimaryMuscle,
    IReadOnlyList<Muscle>? SecondaryMuscles,
    IReadOnlyList<EquipmentType>? Equipment,
    string? VideoUrl,
    string? ImageUrl,
    ExerciseType? ExerciseType = null,
    TrackingType? TrackingType = null,
    bool? IsUnilateral = null,
    Difficulty? Difficulty = null);

/// <summary>
/// <c>MuscleGroup</c> se deriva del músculo principal (<c>UpperBody</c>, <c>LowerBody</c>, <c>Core</c>,
/// <c>FullBody</c>) y no se guarda. <c>IsHidden</c>: el gym actual ocultó este ejercicio del sistema.
/// Los enums salen como texto.
/// </summary>
public sealed record ExerciseResponse(
    Guid Id,
    Guid? GymId,
    bool IsSystem,
    string Name,
    string? Description,
    string PrimaryMuscle,
    string MuscleGroup,
    IReadOnlyList<string> SecondaryMuscles,
    IReadOnlyList<string> Equipment,
    string ExerciseType,
    string TrackingType,
    bool IsUnilateral,
    string Difficulty,
    string? VideoUrl,
    string? ImageUrl,
    bool IsActive,
    bool IsHidden,
    DateTimeOffset CreatedAt);

/// <summary>Duplicar un ejercicio (casi siempre del sistema) como copia del gym.</summary>
public sealed record DuplicateExerciseRequest(bool HideOriginal = false);

