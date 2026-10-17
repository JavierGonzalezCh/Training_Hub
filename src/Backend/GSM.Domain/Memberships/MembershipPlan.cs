using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Memberships;

/// <summary>Plantilla de precio y duración que se asigna a un miembro para crear una <see cref="Membership"/>.</summary>
public sealed class MembershipPlan : BaseEntity, ITenantEntity
{
    private MembershipPlan()
    {
    }

    public Guid GymId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public decimal Price { get; private set; }

    /// <summary>Unidad de la duración: días corridos o meses del calendario.</summary>
    public DurationUnit DurationUnit { get; private set; } = DurationUnit.Days;

    /// <summary>Cuántos días o meses dura (según <see cref="DurationUnit"/>).</summary>
    public int DurationValue { get; private set; }

    /// <summary>Total de días que se puede congelar una membresía de este plan (0 = no se congela). Memberships v2, §3.</summary>
    public int MaxFreezeDays { get; private set; }

    /// <summary>Mínimo de días por congelamiento (evita congelar de a un día).</summary>
    public int MinFreezeDays { get; private set; } = DefaultMinFreezeDays;

    /// <summary>Tipo de plan (MS5): por tiempo o tiquetera.</summary>
    public PlanKind Kind { get; private set; } = PlanKind.Time;

    /// <summary>Cuántas entradas trae. Solo tiqueteras (<see cref="PlanKind.Pass"/>); nulo en planes por tiempo.</summary>
    public int? Entries { get; private set; }

    /// <summary>
    /// En cuántos días se deben usar las entradas. Solo tiqueteras. No es una columna aparte: en una
    /// tiquetera la vigencia es <see cref="DurationValue"/> en días, así el resto del módulo (vista previa,
    /// asignación) la trata como cualquier plan en días.
    /// </summary>
    public int? ValidityDays => Kind == PlanKind.Pass ? DurationValue : null;

    public bool IsPass => Kind == PlanKind.Pass;

    /// <summary>
    /// Cupos del plan (MS7, §9): 1 = individual; más de 1 = plan de grupo (familiar, pareja, corporativo). Un
    /// grupo copia este número al crearse, así que editarlo después no toca a los grupos ya vendidos.
    /// </summary>
    public int Seats { get; private set; } = 1;

    public bool IsGroup => Seats > 1;

    public bool IsActive { get; private set; } = true;

    public const int MaxSeats = 500;

    public const int DefaultMinFreezeDays = 1;

    /// <summary>Días de congelamiento con que nace un plan nuevo (MS4-1); el Owner lo puede dejar en 0.</summary>
    public const int DefaultMaxFreezeDays = 30;

    public const int MaxEntries = 1000;

    public const int MaxFreezeDaysLimit = 365;

    public const int MaxDays = 3650;

    public const int MaxMonths = 120;

    public static MembershipPlan Create(
        Guid gymId, string name, string? description, decimal price, int durationValue,
        DurationUnit durationUnit = DurationUnit.Days, int maxFreezeDays = DefaultMaxFreezeDays, int minFreezeDays = DefaultMinFreezeDays,
        PlanKind kind = PlanKind.Time, int? entries = null, int seats = 1)
    {
        if (gymId == Guid.Empty)
        {
            throw new ValidationDomainException("El plan necesita un gym.");
        }

        var freeze = RequireFreezeRules(maxFreezeDays, minFreezeDays);
        var shape = RequireShape(kind, entries, durationUnit);
        var planSeats = RequireSeats(seats, kind);

        return new MembershipPlan
        {
            GymId = gymId,
            Name = NormalizeName(name),
            Description = Clean(description),
            Price = RequireNonNegative(price),
            DurationUnit = shape.Unit,
            DurationValue = RequireDuration(durationValue, shape.Unit),
            MaxFreezeDays = freeze.Max,
            MinFreezeDays = freeze.Min,
            Kind = kind,
            Entries = shape.Entries,
            Seats = planSeats,
        };
    }

    public void Update(
        string name, string? description, decimal price, int durationValue,
        DurationUnit durationUnit = DurationUnit.Days, int? maxFreezeDays = null, int? minFreezeDays = null,
        PlanKind? kind = null, int? entries = null, int? seats = null)
    {
        if (kind is { } requested && requested != Kind)
        {
            throw new ValidationDomainException(
                "El tipo de un plan no se puede cambiar. Crea otro plan o desactiva este.");
        }

        // En una edición de un plan por tiempo no se mandan entradas; en una tiquetera se conservan si faltan.
        var shape = RequireShape(Kind, Kind == PlanKind.Pass ? entries ?? Entries : null, durationUnit);
        var planSeats = RequireSeats(seats ?? Seats, Kind);
        Name = NormalizeName(name);
        Description = Clean(description);
        Price = RequireNonNegative(price);
        DurationUnit = shape.Unit;
        Entries = shape.Entries;
        Seats = planSeats;
        DurationValue = RequireDuration(durationValue, shape.Unit);
        (MaxFreezeDays, MinFreezeDays) = RequireFreezeRules(maxFreezeDays ?? MaxFreezeDays, minFreezeDays ?? MinFreezeDays);
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    /// <summary>
    /// Forma de cada tipo: la tiquetera trae entradas (1..<see cref="MaxEntries"/>) y su vigencia siempre es en
    /// días; el plan por tiempo no lleva entradas.
    /// </summary>
    private static (DurationUnit Unit, int? Entries) RequireShape(PlanKind kind, int? entries, DurationUnit unit)
    {
        if (kind == PlanKind.Time)
        {
            if (entries is not null)
            {
                throw new ValidationDomainException("Un plan por tiempo no lleva entradas.");
            }

            return (unit, null);
        }

        if (unit != DurationUnit.Days)
        {
            throw new ValidationDomainException("La vigencia de una tiquetera se expresa en días.");
        }

        if (entries is not { } count || count < 1 || count > MaxEntries)
        {
            throw new ValidationDomainException($"Una tiquetera necesita entre 1 y {MaxEntries} entradas.");
        }

        return (DurationUnit.Days, count);
    }

    /// <summary>Cupos entre 1 y <see cref="MaxSeats"/>; un plan de grupo es siempre por tiempo (una tiquetera no se comparte).</summary>
    private static int RequireSeats(int seats, PlanKind kind)
    {
        if (seats < 1 || seats > MaxSeats)
        {
            throw new ValidationDomainException($"Los cupos del plan deben estar entre 1 y {MaxSeats}.");
        }

        if (seats > 1 && kind == PlanKind.Pass)
        {
            throw new ValidationDomainException("Una tiquetera no puede ser un plan de grupo.");
        }

        return seats;
    }

    private static (int Max, int Min) RequireFreezeRules(int max, int min)
    {
        if (max is < 0 or > MaxFreezeDaysLimit)
        {
            throw new ValidationDomainException($"Los días máximos de congelamiento deben estar entre 0 y {MaxFreezeDaysLimit}.");
        }

        if (min < 1 || (max > 0 && min > max))
        {
            throw new ValidationDomainException("El mínimo de días por congelamiento debe ser de al menos 1 y no superar el máximo.");
        }

        return (max, min);
    }

    private static string NormalizeName(string name)
    {
        var value = (name ?? string.Empty).Trim();
        if (value.Length is < 2 or > 100)
        {
            throw new ValidationDomainException("El nombre del plan debe tener entre 2 y 100 caracteres.");
        }

        return value;
    }

    private static decimal RequireNonNegative(decimal price)
        => price >= 0 ? price : throw new ValidationDomainException("El precio no puede ser negativo.");

    private static int RequireDuration(int value, DurationUnit unit)
    {
        var days = unit == DurationUnit.Days;
        if (value < 1)
        {
            throw new ValidationDomainException(
                days ? "La duración debe ser de al menos 1 día." : "La duración debe ser de al menos 1 mes.");
        }

        var max = days ? MaxDays : MaxMonths;
        return value <= max
            ? value
            : throw new ValidationDomainException(
                days ? $"La duración no puede superar {MaxDays} días." : $"La duración no puede superar {MaxMonths} meses.");
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
