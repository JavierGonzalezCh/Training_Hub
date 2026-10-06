using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Payments;

/// <summary>
/// Asiento de caja. Inmutable: los pagos no se editan ni se borran. Una corrección es otro
/// <see cref="Payment"/> con monto negativo y concepto explícito.
/// </summary>
public sealed class Payment : BaseEntity, ITenantEntity
{
    private Payment()
    {
    }

    public Guid GymId { get; private set; }

    /// <summary><c>null</c> para ingresos sin miembro (venta de producto, clase suelta a un visitante).</summary>
    public Guid? MemberId { get; private set; }

    /// <summary>Vínculo opcional a la membresía que este pago cubre. No tiene efecto sobre ella.</summary>
    public Guid? MembershipId { get; private set; }

    /// <summary>Monto en COP. Puede ser negativo (asiento de ajuste). Nunca cero.</summary>
    public decimal Amount { get; private set; }

    public PaymentMethod Method { get; private set; }

    public string Concept { get; private set; } = string.Empty;

    public string? Notes { get; private set; }

    /// <summary>Usuario que registró el asiento. Siempre del <c>ICurrentUserService</c>, nunca del body.</summary>
    public Guid RegisteredBy { get; private set; }

    /// <summary>Instante en que se registró el asiento (no la "fecha valor"). Inmutable, del reloj.</summary>
    public DateTimeOffset PaidAt { get; private set; }

    /// <summary>
    /// Pago que este asiento anula, o <c>null</c> si es un pago normal. Único: un pago se anula una
    /// sola vez. El pago original no cambia; la anulación es este asiento inverso.
    /// </summary>
    public Guid? ReversesPaymentId { get; private set; }

    public static Payment Create(
        Guid gymId,
        Guid? memberId,
        Guid? membershipId,
        decimal amount,
        PaymentMethod method,
        string concept,
        string? notes,
        Guid registeredBy,
        DateTimeOffset paidAt)
    {
        if (gymId == Guid.Empty)
        {
            throw new ValidationDomainException("El pago necesita un gym.");
        }

        if (registeredBy == Guid.Empty)
        {
            throw new ValidationDomainException("El pago necesita el usuario que lo registra.");
        }

        if (amount == 0)
        {
            throw new ValidationDomainException("El monto no puede ser cero.");
        }

        if (membershipId is not null && memberId is null)
        {
            throw new ValidationDomainException("Un pago con membresía debe indicar también el miembro.");
        }

        var trimmedConcept = (concept ?? string.Empty).Trim();
        if (trimmedConcept.Length is < 1 or > 200)
        {
            throw new ValidationDomainException("El concepto es obligatorio y no puede exceder 200 caracteres.");
        }

        return new Payment
        {
            GymId = gymId,
            MemberId = memberId,
            MembershipId = membershipId,
            Amount = decimal.Round(amount, 2),
            Method = method,
            Concept = trimmedConcept,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            RegisteredBy = registeredBy,
            PaidAt = paidAt,
        };
    }

    /// <summary>
    /// Asiento inverso de <paramref name="original"/>: mismo método, miembro y membresía, monto
    /// opuesto. Así la caja del día de la anulación cuadra con lo que de verdad salió del cajón.
    /// </summary>
    public static Payment CreateReversal(
        Payment original, string reason, Guid registeredBy, DateTimeOffset paidAt)
    {
        ArgumentNullException.ThrowIfNull(original);

        if (original.ReversesPaymentId is not null)
        {
            throw new ValidationDomainException("Una anulación no se puede anular.");
        }

        var trimmedReason = (reason ?? string.Empty).Trim();
        if (trimmedReason.Length is < 3 or > 500)
        {
            throw new ValidationDomainException("El motivo de la anulación es obligatorio (3 a 500 caracteres).");
        }

        var concept = $"Anulación: {original.Concept}";
        if (concept.Length > 200)
        {
            concept = concept[..200];
        }

        var reversal = Create(
            original.GymId,
            original.MemberId,
            original.MembershipId,
            -original.Amount,
            original.Method,
            concept,
            trimmedReason,
            registeredBy,
            paidAt);
        reversal.ReversesPaymentId = original.Id;
        return reversal;
    }
}
