using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Classes;

/// <summary>
/// Definición de una clase del gimnasio. Se repite semanalmente sin fecha de fin
/// (<see cref="IsRecurring"/> con <see cref="DaysOfWeek"/>) o existe una sola vez en
/// <see cref="ScheduledDate"/>. Las ocurrencias concretas son <c>ClassSession</c> (etapa 10.2),
/// que no se materializan por adelantado sino cuando hay algo que guardar.
/// </summary>
public sealed class GymClass : BaseEntity, ITenantEntity
{
    private GymClass()
    {
    }

    public Guid GymId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>Texto libre; sin entidad ni cuenta.</summary>
    public string? InstructorName { get; private set; }

    /// <summary><c>null</c> = sin reserva, entrada libre por orden de llegada. Con valor, ≥ 1.</summary>
    public int? Capacity { get; private set; }

    /// <summary>Hora local del gym.</summary>
    public TimeOnly StartTime { get; private set; }

    public int DurationMinutes { get; private set; }

    public bool IsRecurring { get; private set; }

    /// <summary>
    /// Solo si <see cref="IsRecurring"/>: al menos un día. Vacío si no. <c>List&lt;T&gt;</c> por
    /// exigencia del proveedor (array nativo de Postgres); solo se reemplaza desde <see cref="Apply"/>.
    /// </summary>
    public List<ClassDayOfWeek> DaysOfWeek { get; private set; } = [];

    /// <summary>Solo si <b>no</b> es recurrente: la fecha única.</summary>
    public DateOnly? ScheduledDate { get; private set; }

    /// <summary>Desde cuándo aplica la recurrencia (día de negocio del alta).</summary>
    public DateOnly StartsOn { get; private set; }

    public bool IsActive { get; private set; } = true;

    /// <summary>Cierra la recurrencia; las sesiones pasadas se conservan.</summary>
    public DateOnly? DeactivatedOn { get; private set; }

    public static GymClass Create(Guid gymId, DateOnly startsOn, GymClassDraft draft)
    {
        if (gymId == Guid.Empty)
        {
            throw new ValidationDomainException("La clase necesita un gym.");
        }

        var gymClass = new GymClass
        {
            GymId = gymId,
            StartsOn = startsOn,
        };

        gymClass.Apply(draft);
        return gymClass;
    }

    /// <summary>Reemplaza la definición. No cambia <see cref="StartsOn"/>: es el ancla histórica de la serie.</summary>
    public void Update(GymClassDraft draft) => Apply(draft);

    public void Deactivate(DateOnly on)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        DeactivatedOn = on;
    }

    private void Apply(GymClassDraft draft)
    {
        var days = (draft.DaysOfWeek ?? []).Distinct().ToList();

        if (draft.IsRecurring)
        {
            if (days.Count == 0)
            {
                throw new ValidationDomainException(
                    "Una clase recurrente necesita al menos un día de la semana.");
            }

            if (draft.ScheduledDate is not null)
            {
                throw new ValidationDomainException(
                    "Una clase recurrente no lleva fecha única.");
            }
        }
        else
        {
            if (draft.ScheduledDate is null)
            {
                throw new ValidationDomainException(
                    "Una clase no recurrente necesita una fecha única.");
            }

            if (days.Count > 0)
            {
                throw new ValidationDomainException(
                    "Una clase no recurrente no lleva días de la semana.");
            }
        }

        if (draft.Capacity is { } capacity && capacity < 1)
        {
            throw new ValidationDomainException("El cupo debe ser al menos 1, o nulo para entrada libre.");
        }

        if (draft.DurationMinutes is < 5 or > 480)
        {
            throw new ValidationDomainException("La duración debe estar entre 5 y 480 minutos.");
        }

        Name = NormalizeName(draft.Name);
        Description = Clean(draft.Description, 1000);
        InstructorName = Clean(draft.InstructorName, 120);
        Capacity = draft.Capacity;
        StartTime = draft.StartTime;
        DurationMinutes = draft.DurationMinutes;
        IsRecurring = draft.IsRecurring;
        ScheduledDate = draft.ScheduledDate;
        DaysOfWeek = days;
    }

    private static string NormalizeName(string name)
    {
        var value = (name ?? string.Empty).Trim();
        if (value.Length is < 2 or > 120)
        {
            throw new ValidationDomainException("El nombre de la clase debe tener entre 2 y 120 caracteres.");
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
