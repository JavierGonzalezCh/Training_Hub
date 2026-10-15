using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.SuperAdmin;

/// <summary>
/// Plan del SaaS (SuperAdmin v2, §V1): define un límite de miembros; todos los planes traen todos los
/// módulos. Lo edita el SuperAdmin. El precio es orientativo: el real es el de cada suscripción.
/// </summary>
public sealed class SaasPlan : BaseEntity
{
    public const int NameMaxLength = 80;

    private SaasPlan()
    {
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Máximo de miembros que cuentan a la vez. <c>null</c> = sin límite.</summary>
    public int? MemberLimit { get; private set; }

    public decimal SuggestedPrice { get; private set; }

    public bool IsActive { get; private set; } = true;

    public static SaasPlan Create(string name, int? memberLimit, decimal suggestedPrice)
    {
        var plan = new SaasPlan();
        plan.Apply(name, memberLimit, suggestedPrice);
        return plan;
    }

    public void Update(string name, int? memberLimit, decimal suggestedPrice, bool isActive)
    {
        Apply(name, memberLimit, suggestedPrice);
        IsActive = isActive;
    }

    private void Apply(string name, int? memberLimit, decimal suggestedPrice)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationDomainException("El plan necesita un nombre.");
        }

        if (name.Trim().Length > NameMaxLength)
        {
            throw new ValidationDomainException($"El nombre del plan no puede pasar de {NameMaxLength} caracteres.");
        }

        if (memberLimit is < 1)
        {
            throw new ValidationDomainException("El límite de miembros debe ser de al menos 1 (o vacío para ilimitado).");
        }

        if (suggestedPrice < 0)
        {
            throw new ValidationDomainException("El precio sugerido no puede ser negativo.");
        }

        Name = name.Trim();
        MemberLimit = memberLimit;
        SuggestedPrice = decimal.Round(suggestedPrice, 2);
    }
}
