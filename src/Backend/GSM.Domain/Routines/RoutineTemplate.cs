using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Routines;

/// <summary>
/// Plantilla de rutina de un gym (R11): una rutina reutilizable que se copia al asignarla a un miembro. Guarda el
/// contenido (días, líneas y series) como un documento JSON ya validado por el caso de uso: copiar es releerlo, no
/// hay vínculo vivo con las rutinas que salen de ella, y editarla no toca a las ya asignadas.
/// </summary>
public sealed class RoutineTemplate : BaseEntity, ITenantEntity
{
    public const int MaxNameLength = 120;
    public const int MaxDescriptionLength = 500;
    public const int MaxImageUrlLength = 500;
    public const int MaxBaseKeyLength = 60;

    private RoutineTemplate()
    {
    }

    public Guid GymId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary><c>rotating</c> o <c>weekly</c> (copia del modo del contenido, para listar sin leerlo).</summary>
    public string ScheduleMode { get; private set; } = "rotating";

    /// <summary>Documento JSON con el cuerpo de la rutina (<c>SaveRoutineRequest</c> sin ids).</summary>
    public string Content { get; private set; } = "{}";

    /// <summary>Días de entrenamiento (sin descansos) y líneas de ejercicio, para el listado.</summary>
    public int DayCount { get; private set; }

    public int ExerciseCount { get; private set; }

    public Guid CreatedBy { get; private set; }

    /// <summary>Imagen de portada (URL absoluta o ruta de una imagen de muestra). Nula: la interfaz usa una genérica.</summary>
    public string? ImageUrl { get; private set; }

    /// <summary>
    /// Clave de la plantilla base de la que nació (p. ej. <c>full-body-beginner</c>); nula si la creó el gym. Hace
    /// idempotente la siembra: un gym tiene como máximo una copia de cada plantilla base.
    /// </summary>
    public string? BaseKey { get; private set; }

    public static RoutineTemplate Create(
        Guid gymId, string name, string? description, string scheduleMode, string content,
        int dayCount, int exerciseCount, Guid createdBy)
    {
        if (gymId == Guid.Empty)
        {
            throw new ValidationDomainException("La plantilla necesita un gym.");
        }

        var template = new RoutineTemplate { GymId = gymId, CreatedBy = createdBy };
        template.Update(name, description, scheduleMode, content, dayCount, exerciseCount);
        return template;
    }

    /// <summary>Copia de una plantilla base del sistema para un gym.</summary>
    public static RoutineTemplate CreateFromBase(
        Guid gymId, string baseKey, string name, string? description, string scheduleMode, string content,
        int dayCount, int exerciseCount, string? imageUrl, Guid createdBy)
    {
        var key = (baseKey ?? string.Empty).Trim();
        if (key.Length is 0 or > MaxBaseKeyLength)
        {
            throw new ValidationDomainException("La clave de la plantilla base no es válida.");
        }

        var template = Create(gymId, name, description, scheduleMode, content, dayCount, exerciseCount, createdBy);
        template.BaseKey = key;
        template.SetImage(imageUrl);
        return template;
    }

    public void SetImage(string? imageUrl)
    {
        var url = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl.Trim();
        if (url is { Length: > MaxImageUrlLength })
        {
            throw new ValidationDomainException(
                $"La dirección de la imagen admite como máximo {MaxImageUrlLength} caracteres.");
        }

        ImageUrl = url;
    }

    public void Update(
        string name, string? description, string scheduleMode, string content, int dayCount, int exerciseCount)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length is < 2 or > MaxNameLength)
        {
            throw new ValidationDomainException(
                $"El nombre de la plantilla debe tener entre 2 y {MaxNameLength} caracteres.");
        }

        var desc = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (desc is { Length: > MaxDescriptionLength })
        {
            throw new ValidationDomainException(
                $"La descripción admite como máximo {MaxDescriptionLength} caracteres.");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ValidationDomainException("La plantilla no tiene contenido.");
        }

        Name = trimmed;
        Description = desc;
        ScheduleMode = scheduleMode;
        Content = content;
        DayCount = dayCount;
        ExerciseCount = exerciseCount;
    }
}
