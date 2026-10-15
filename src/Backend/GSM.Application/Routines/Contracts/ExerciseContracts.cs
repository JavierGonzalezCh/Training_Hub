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
/// Los enums salen como texto. <c>IsAvailableHere</c>: ¿se puede hacer hoy en este gym? (Equipment §5; sin
/// inventario siempre <c>true</c>). <c>Machines</c>: las máquinas del gym a las que está ligado (vacío si no hay).
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
    DateTimeOffset CreatedAt,
    bool IsAvailableHere = true,
    IReadOnlyList<EquipmentMachineInfo>? Machines = null);

/// <summary>Duplicar un ejercicio (casi siempre del sistema) como copia del gym.</summary>
public sealed record DuplicateExerciseRequest(bool HideOriginal = false);


/// <summary>
/// Una máquina del gym a la que está ligado un ejercicio. <c>PhotoUrl</c> ya resuelve foto propia → imagen del
/// modelo (<c>null</c> = la UI pone el ícono del tipo). <c>Type</c> y <c>Status</c> salen como texto
/// (<c>Available</c>, <c>Maintenance</c>, <c>OutOfService</c>).
/// </summary>
public sealed record EquipmentMachineInfo(
    Guid Id,
    string Name,
    string Type,
    string? PhotoUrl,
    string? Zone,
    string? Instructions,
    string Status,
    DateOnly? ExpectedBackOn);

/// <summary>
/// Disponibilidad de un ejercicio en el gym actual y las máquinas que usa. Con <c>Machines</c> vacío y
/// <c>IsAvailable = false</c>, el gym no tiene los tipos de equipo que pide el ejercicio.
/// </summary>
public sealed record ExerciseEquipmentInfo(bool IsAvailable, IReadOnlyList<EquipmentMachineInfo> Machines);

/// <summary>Modelo del catálogo de máquinas al que el sistema liga un ejercicio. <c>Type</c> sale como texto.</summary>
public sealed record ExerciseLinkedModel(Guid Id, string Name, string Type);

/// <summary>
/// Cómo está ligado un ejercicio. <c>Models</c>: ligas del sistema (SuperAdmin). <c>ItemIds</c>: ligas propias del
/// gym; si hay alguna, solo esas cuentan en este gym (sirven para corregir el ligado automático por modelo).
/// <c>Machines</c>: las máquinas del gym que resultan de eso.
/// </summary>
public sealed record ExerciseEquipmentLinksResponse(
    IReadOnlyList<ExerciseLinkedModel> Models,
    IReadOnlyList<Guid> ItemIds,
    IReadOnlyList<EquipmentMachineInfo> Machines,
    bool IsAvailableHere);

/// <summary>
/// Una alternativa a un ejercicio (R5). <c>Source</c>: <c>recommended</c> (la guardó el SuperAdmin o el gym) o
/// <c>suggested</c> (calculada al vuelo). Las recomendadas van primero. <c>IsAvailableHere</c> es falso si hoy no se
/// puede hacer en el gym (máquina en mantenimiento o equipo que no tiene); las sugeridas nunca salen así, pero una
/// recomendada sí puede. Los enums salen como texto.
/// </summary>
public sealed record ExerciseAlternativeResponse(
    Guid Id,
    string Name,
    string Source,
    string PrimaryMuscle,
    string MuscleGroup,
    string ExerciseType,
    string TrackingType,
    bool IsUnilateral,
    string Difficulty,
    string? ImageUrl,
    bool IsAvailableHere,
    IReadOnlyList<EquipmentMachineInfo> Machines);

/// <summary>SuperAdmin: deja el ejercicio del sistema ligado exactamente a estos modelos (máximo 20; vacío = ninguno).</summary>
public sealed record SetExerciseEquipmentModelsRequest(IReadOnlyList<Guid> ModelIds);

/// <summary>Staff: deja el ejercicio ligado exactamente a estas máquinas del gym (máximo 20; vacío = volver al ligado por modelo).</summary>
public sealed record SetExerciseEquipmentItemsRequest(IReadOnlyList<Guid> ItemIds);
