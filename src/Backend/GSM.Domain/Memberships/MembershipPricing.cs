using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Memberships;

/// <summary>
/// Precio de una membresía al asignarla (Memberships v2 §4 y §6): precio acordado con descuento y
/// motivo, y matrícula. Se resuelve una sola vez, con las reglas de dominio, y la membresía lo guarda.
/// </summary>
public sealed record MembershipPricing(
    decimal AgreedPrice,
    string? DiscountReason,
    decimal EnrollmentFee,
    string? EnrollmentWaivedReason)
{
    public decimal Total => AgreedPrice + EnrollmentFee;

    /// <summary>
    /// <paramref name="requestedPrice"/> nulo = el del plan. No puede superar al del plan; si es menor,
    /// el motivo es obligatorio. La matrícula (<paramref name="enrollmentFee"/>) solo cuenta si
    /// <paramref name="enrollmentApplies"/> (miembro sin membresías previas); exonerarla exige motivo.
    /// </summary>
    public static MembershipPricing Resolve(
        decimal planPrice,
        decimal? requestedPrice,
        string? discountReason,
        decimal enrollmentFee,
        bool enrollmentApplies,
        bool waiveEnrollment,
        string? waivedReason)
    {
        var agreed = decimal.Round(requestedPrice ?? planPrice, 2);
        if (agreed < 0)
        {
            throw new ValidationDomainException("El precio acordado no puede ser negativo.");
        }

        if (agreed > planPrice)
        {
            throw new ValidationDomainException("El precio acordado no puede superar el precio del plan.");
        }

        string? discount = null;
        if (agreed < planPrice)
        {
            discount = RequireReason(discountReason, "El motivo del descuento es obligatorio (3 a 500 caracteres).");
        }

        var fee = 0m;
        string? waived = null;
        if (enrollmentApplies && enrollmentFee > 0)
        {
            if (waiveEnrollment)
            {
                waived = RequireReason(waivedReason, "El motivo para exonerar la matrícula es obligatorio (3 a 500 caracteres).");
            }
            else
            {
                fee = decimal.Round(enrollmentFee, 2);
            }
        }

        return new MembershipPricing(agreed, discount, fee, waived);
    }

    private static string RequireReason(string? reason, string message)
    {
        var trimmed = (reason ?? string.Empty).Trim();
        if (trimmed.Length is < 3 or > 500)
        {
            throw new ValidationDomainException(message);
        }

        return trimmed;
    }
}
