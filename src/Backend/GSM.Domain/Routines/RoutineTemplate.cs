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
