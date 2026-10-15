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

    public bool IsActive { get; private set; } = true;

    public const int MaxDays = 3650;

    public const int MaxMonths = 120;

    public static MembershipPlan Create(
        Guid gymId, string name, string? description, decimal price, int durationValue,
        DurationUnit durationUnit = DurationUnit.Days)
    {
        if (gymId == Guid.Empty)
        {
            throw new ValidationDomainException("El plan necesita un gym.");
        }

        return new MembershipPlan
        {
            GymId = gymId,
            Name = NormalizeName(name),
            Description = Clean(description),
            Price = RequireNonNegative(price),
            DurationUnit = durationUnit,
            DurationValue = RequireDuration(durationValue, durationUnit),
        };
    }

    public void Update(
        string name, string? description, decimal price, int durationValue,
        DurationUnit durationUnit = DurationUnit.Days)
    {
        Name = NormalizeName(name);
        Description = Clean(description);
        Price = RequireNonNegative(price);
        DurationUnit = durationUnit;
        DurationValue = RequireDuration(durationValue, durationUnit);
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

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
