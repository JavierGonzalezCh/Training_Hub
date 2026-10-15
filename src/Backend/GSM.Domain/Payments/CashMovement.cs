using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Payments;

/// <summary>Tipo de movimiento de efectivo. Enum nativo <c>payments.cash_movement_type</c>.</summary>
public enum CashMovementType
{
    /// <summary>Sacar plata de la caja (llevarla al banco, pagar un domicilio).</summary>
    Withdrawal,

    /// <summary>Meter plata a la caja (cambio).</summary>
    Deposit,
}

/// <summary>
/// Retiro o ingreso de efectivo a mitad de turno (Payments v2 §4.2). No es ingreso ni gasto del gym: solo
/// mueve la caja del turno.
/// </summary>
public sealed class CashMovement : BaseEntity, ITenantEntity
{
    public const int ReasonMaxLength = 300;

    private CashMovement()
    {
    }

    public Guid GymId { get; private set; }

    public Guid ShiftId { get; private set; }

    public CashMovementType Type { get; private set; }

    /// <summary>Siempre positivo; el tipo dice si entra o sale.</summary>
    public decimal Amount { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public static CashMovement Create(
        Guid gymId, Guid shiftId, CashMovementType type, decimal amount, string reason, Guid createdBy, DateTimeOffset occurredAt)
    {
        if (gymId == Guid.Empty || shiftId == Guid.Empty || createdBy == Guid.Empty)
        {
            throw new ValidationDomainException("El movimiento necesita gym, turno y quien lo hace.");
        }

        if (amount <= 0)
        {
            throw new ValidationDomainException("El monto del movimiento debe ser mayor que cero.");
        }

        var trimmed = (reason ?? string.Empty).Trim();
        if (trimmed.Length is < 3 or > ReasonMaxLength)
        {
            throw new ValidationDomainException($"El motivo es obligatorio (3 a {ReasonMaxLength} caracteres).");
        }

        return new CashMovement
        {
            GymId = gymId,
            ShiftId = shiftId,
            Type = type,
            Amount = decimal.Round(amount, 2),
            Reason = trimmed,
            CreatedBy = createdBy,
            OccurredAt = occurredAt,
        };
    }
}
