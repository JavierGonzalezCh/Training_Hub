using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Payments;

/// <summary>Estado de una solicitud de anulación. Enum nativo <c>payments.void_request_status</c>.</summary>
public enum VoidRequestStatus
{
    Pending,
    Approved,
    Rejected,
}

/// <summary>
/// Solicitud de anulación de un pago, hecha por quien no tiene el permiso <c>VoidPayments</c>. La aprueba o
/// rechaza el Owner o quien lo tenga. Mientras está pendiente no admite otra para el mismo pago.
/// </summary>
public sealed class VoidRequest : BaseEntity, ITenantEntity
{
    public const int ReasonMaxLength = 500;

    private VoidRequest()
    {
    }

    public Guid GymId { get; private set; }

    public Guid PaymentId { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public Guid RequestedBy { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    public VoidRequestStatus Status { get; private set; } = VoidRequestStatus.Pending;

    public Guid? ResolvedBy { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public static VoidRequest Create(Guid gymId, Guid paymentId, string reason, Guid requestedBy, DateTimeOffset now)
    {
        if (gymId == Guid.Empty || paymentId == Guid.Empty || requestedBy == Guid.Empty)
        {
            throw new ValidationDomainException("La solicitud necesita gym, pago y solicitante.");
        }

        var trimmed = (reason ?? string.Empty).Trim();
        if (trimmed.Length is < 3 or > ReasonMaxLength)
        {
            throw new ValidationDomainException(
                $"El motivo de la solicitud es obligatorio (3 a {ReasonMaxLength} caracteres).");
        }

        return new VoidRequest
        {
            GymId = gymId,
            PaymentId = paymentId,
            Reason = trimmed,
            RequestedBy = requestedBy,
            RequestedAt = now,
        };
    }

    public void Approve(Guid userId, DateTimeOffset now) => Resolve(VoidRequestStatus.Approved, userId, now);

    public void Reject(Guid userId, DateTimeOffset now) => Resolve(VoidRequestStatus.Rejected, userId, now);

    private void Resolve(VoidRequestStatus status, Guid userId, DateTimeOffset now)
    {
        if (Status != VoidRequestStatus.Pending)
        {
            throw new ConflictException("Esta solicitud ya fue resuelta.");
        }

        Status = status;
        ResolvedBy = userId;
        ResolvedAt = now;
    }
}
