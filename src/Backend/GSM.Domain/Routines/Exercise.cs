using GSM.Domain.Common;
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

    public List<Equipment> Equipment { get; private set; } = [];

    public string? VideoUrl { get; private set; }

    public string? ImageUrl { get; private set; }

    public bool IsActive { get; private set; } = true;

    /// <summary>Del catálogo del sistema (sin gym dueño).</summary>
    public bool IsSystem => GymId is null;

    public static Exercise Create(
        Guid? gymId,
        string name,
        string? description,
        Muscle primaryMuscle,
        IEnumerable<Muscle>? secondaryMuscles,
        IEnumerable<Equipment>? equipment,
        string? videoUrl,
        string? imageUrl)
    {
        var exercise = new Exercise
        {
            GymId = gymId == Guid.Empty ? null : gymId,
        };

        exercise.Apply(name, description, primaryMuscle, secondaryMuscles, equipment, videoUrl, imageUrl);
        return exercise;
    }

    public void Update(
        string name,
        string? description,
        Muscle primaryMuscle,
        IEnumerable<Muscle>? secondaryMuscles,
        IEnumerable<Equipment>? equipment,
        string? videoUrl,
        string? imageUrl)
        => Apply(name, description, primaryMuscle, secondaryMuscles, equipment, videoUrl, imageUrl);

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    private void Apply(
        string name,
        string? description,
        Muscle primaryMuscle,
        IEnumerable<Muscle>? secondaryMuscles,
        IEnumerable<Equipment>? equipment,
        string? videoUrl,
        string? imageUrl)
    {
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
    }

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
