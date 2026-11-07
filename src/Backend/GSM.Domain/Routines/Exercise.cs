using GSM.Domain.Common;
using GSM.Domain.Equipment;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Routines;

/// <summary>
/// Ejercicio del catálogo híbrido. <see cref="GymId"/> <c>null</c> = ejercicio del sistema,
/// visible para todos los gyms; con valor = propio de ese gym.
/// </summary>
/// <remarks>
/// **No implementa <see cref="ITenantEntity"/>:** el Global Query Filter compara por igualdad
/// estricta y aquí hace falta <c>gym_id IS NULL OR gym_id = @current</c>. El filtrado va explícito
/// en <c>ExerciseRepository</c>. Las listas de enums se mapean a arrays nativos de Postgres; son
/// <c>List&lt;T&gt;</c> por exigencia del proveedor, pero solo se reemplazan desde los métodos.
/// </remarks>
public sealed class Exercise : BaseEntity
{
    private Exercise()
    {
    }

    public Guid? GymId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public Muscle PrimaryMuscle { get; private set; }

    public List<Muscle> SecondaryMuscles { get; private set; } = [];

    public List<EquipmentType> Equipment { get; private set; } = [];

    public string? VideoUrl { get; private set; }

    public string? ImageUrl { get; private set; }

    public ExerciseType ExerciseType { get; private set; } = ExerciseType.Strength;

    /// <summary>Qué se mide en cada serie. Sugerido por <see cref="ExerciseType"/> y editable.</summary>
    public TrackingType TrackingType { get; private set; } = TrackingType.RepsWeight;

    /// <summary>Se hace por lado: las repeticiones planeadas y registradas son por lado.</summary>
    public bool IsUnilateral { get; private set; }

    /// <summary>Sentadilla, press de banca o peso muerto: cuenta para el logro «Club de la Fuerza». Nulo = ninguno.</summary>
    public PowerLift? PowerLift { get; private set; }

    public void MarkPowerLift(PowerLift? lift) => PowerLift = lift;

    public Difficulty Difficulty { get; private set; } = Difficulty.Intermediate;

    public bool IsActive { get; private set; } = true;

    /// <summary>Zona del cuerpo, derivada del músculo principal; no se guarda.</summary>
    public MuscleGroup MuscleGroup => MuscleGroups.Of(PrimaryMuscle);

    /// <summary>Del catálogo del sistema (sin gym dueño).</summary>
    public bool IsSystem => GymId is null;

    public static Exercise Create(
        Guid? gymId,
        string name,
        string? description,
        Muscle primaryMuscle,
        IEnumerable<Muscle>? secondaryMuscles,
        IEnumerable<EquipmentType>? equipment,
        string? videoUrl,
        string? imageUrl,
        ExerciseType exerciseType = ExerciseType.Strength,
        TrackingType? trackingType = null,
        bool isUnilateral = false,
        Difficulty difficulty = Difficulty.Intermediate)
    {
        var exercise = new Exercise
        {
            GymId = gymId == Guid.Empty ? null : gymId,
        };

        exercise.Apply(name, description, primaryMuscle, secondaryMuscles, equipment, videoUrl, imageUrl,
            exerciseType, trackingType, isUnilateral, difficulty);
        return exercise;
    }

    public void Update(
        string name,
        string? description,
        Muscle primaryMuscle,
        IEnumerable<Muscle>? secondaryMuscles,
        IEnumerable<EquipmentType>? equipment,
        string? videoUrl,
        string? imageUrl,
        ExerciseType exerciseType = ExerciseType.Strength,
        TrackingType? trackingType = null,
        bool isUnilateral = false,
        Difficulty difficulty = Difficulty.Intermediate)
        => Apply(name, description, primaryMuscle, secondaryMuscles, equipment, videoUrl, imageUrl,
            exerciseType, trackingType, isUnilateral, difficulty);

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    private void Apply(
        string name,
        string? description,
        Muscle primaryMuscle,
        IEnumerable<Muscle>? secondaryMuscles,
        IEnumerable<EquipmentType>? equipment,
        string? videoUrl,
        string? imageUrl,
        ExerciseType exerciseType,
        TrackingType? trackingType,
        bool isUnilateral,
        Difficulty difficulty)
    {
        if (!Enum.IsDefined(exerciseType) || !Enum.IsDefined(difficulty)
            || (trackingType is { } t && !Enum.IsDefined(t)))
        {
            throw new ValidationDomainException("Tipo, medición o nivel del ejercicio no válidos.");
        }

        var secondary = (secondaryMuscles ?? []).Distinct().ToList();
        if (secondary.Contains(primaryMuscle))
        {
            throw new ValidationDomainException(
                "Un músculo secundario no puede ser el músculo primario.");
        }

        Name = NormalizeName(name);
        Description = Clean(description, 1000);
        PrimaryMuscle = primaryMuscle;
        SecondaryMuscles = secondary;
        Equipment = (equipment ?? []).Distinct().ToList();
        VideoUrl = Clean(videoUrl, 2048);
        ImageUrl = Clean(imageUrl, 2048);
        ExerciseType = exerciseType;
        TrackingType = trackingType ?? ExerciseDefaults.SuggestedTracking(exerciseType);
        IsUnilateral = isUnilateral;
        Difficulty = difficulty;
    }

    /// <summary>Copia del ejercicio para un gym: todos los campos y las mismas URLs de medios.</summary>
    public Exercise DuplicateFor(Guid gymId, string name)
        => Create(gymId, name, Description, PrimaryMuscle, SecondaryMuscles, Equipment, VideoUrl, ImageUrl,
            ExerciseType, TrackingType, IsUnilateral, Difficulty);

    private static string NormalizeName(string name)
    {
        var value = (name ?? string.Empty).Trim();
        if (value.Length is < 2 or > 120)
        {
            throw new ValidationDomainException(
                "El nombre del ejercicio debe tener entre 2 y 120 caracteres.");
        }

        return value;
    }

    private static string? Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ValidationDomainException($"El texto no puede exceder {maxLength} caracteres.");
        }

        return trimmed;
    }
}
