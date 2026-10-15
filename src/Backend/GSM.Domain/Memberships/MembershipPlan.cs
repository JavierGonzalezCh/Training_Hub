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

    public bool IsActive { get; private set; } = true;

    public const int DefaultMinFreezeDays = 1;

    public const int MaxFreezeDaysLimit = 365;

    public const int MaxDays = 3650;

    public const int MaxMonths = 120;

    public static MembershipPlan Create(
        Guid gymId, string name, string? description, decimal price, int durationValue,
        DurationUnit durationUnit = DurationUnit.Days, int maxFreezeDays = 0, int minFreezeDays = DefaultMinFreezeDays)
    {
        if (gymId == Guid.Empty)
        {
            throw new ValidationDomainException("El plan necesita un gym.");
        }

        var freeze = RequireFreezeRules(maxFreezeDays, minFreezeDays);

        return new MembershipPlan
        {
            GymId = gymId,
            Name = NormalizeName(name),
            Description = Clean(description),
            Price = RequireNonNegative(price),
            DurationUnit = durationUnit,
            DurationValue = RequireDuration(durationValue, durationUnit),
            MaxFreezeDays = freeze.Max,
            MinFreezeDays = freeze.Min,
        };
    }

    public void Update(
        string name, string? description, decimal price, int durationValue,
        DurationUnit durationUnit = DurationUnit.Days, int? maxFreezeDays = null, int? minFreezeDays = null)
    {
        Name = NormalizeName(name);
        Description = Clean(description);
        Price = RequireNonNegative(price);
        DurationUnit = durationUnit;
        DurationValue = RequireDuration(durationValue, durationUnit);
        (MaxFreezeDays, MinFreezeDays) = RequireFreezeRules(maxFreezeDays ?? MaxFreezeDays, minFreezeDays ?? MinFreezeDays);
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

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
