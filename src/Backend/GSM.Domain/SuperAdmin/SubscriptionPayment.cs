using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Payments;

namespace GSM.Domain.SuperAdmin;

/// <summary>
/// Pago de un gym al SaaS. <b>Inmutable</b>, igual que la caja del gym (etapa 5): no se edita ni se
/// borra; una corrección es otro asiento con monto negativo y concepto explícito. Comparte el enum
/// <see cref="PaymentMethod"/> con Payments y nada más — son bolsillos distintos.
/// </summary>
public sealed class SubscriptionPayment : BaseEntity
{
    private SubscriptionPayment()
    {
    }

    public Guid GymId { get; private set; }

    /// <summary>Monto en COP. Puede ser negativo (ajuste). Nunca cero.</summary>
    public decimal Amount { get; private set; }

    public PaymentMethod Method { get; private set; }

    public DateOnly CoversFrom { get; private set; }

    public DateOnly CoversThrough { get; private set; }

    /// <summary>Instante del registro, del reloj. No es "fecha valor".</summary>
    public DateTimeOffset PaidAt { get; private set; }

    /// <summary>El superadmin que lo registró. Siempre de <c>ICurrentUserService</c>, nunca del body.</summary>
    public Guid RegisteredBy { get; private set; }

    /// <summary>Número de transferencia, comprobante.</summary>
    public string? Reference { get; private set; }

    public string? Notes { get; private set; }

    public static SubscriptionPayment Create(
        Guid gymId,
        decimal amount,
        PaymentMethod method,
        DateOnly coversFrom,
        DateOnly coversThrough,
        DateTimeOffset paidAt,
        Guid registeredBy,
        string? reference,
        string? notes)
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

        if (coversThrough < coversFrom)
        {
            throw new ValidationDomainException("El periodo cubierto no puede terminar antes de empezar.");
        }

        var trimmedReference = Trim(reference);
        if (trimmedReference is { Length: > 200 })
        {
            throw new ValidationDomainException("La referencia no puede exceder 200 caracteres.");
        }

        var trimmedNotes = Trim(notes);
        if (trimmedNotes is { Length: > 1000 })
        {
            throw new ValidationDomainException("Las notas no pueden exceder 1000 caracteres.");
        }

        return new SubscriptionPayment
        {
            GymId = gymId,
            Amount = decimal.Round(amount, 2),
            Method = method,
            CoversFrom = coversFrom,
            CoversThrough = coversThrough,
            PaidAt = paidAt,
            RegisteredBy = registeredBy,
            Reference = trimmedReference,
            Notes = trimmedNotes,
        };
    }

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
