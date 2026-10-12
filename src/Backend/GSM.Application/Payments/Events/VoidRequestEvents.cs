using GSM.Application.Common.Events;

namespace GSM.Application.Payments.Events;

/// <summary>Alguien sin el permiso de anular pidió anular un pago. Notifications avisa a quien puede aprobarla.</summary>
public sealed record VoidRequested(
    Guid GymId,
    Guid VoidRequestId,
    Guid PaymentId,
    Guid RequestedBy,
    Guid? MemberId,
    string PaymentConcept,
    decimal PaymentAmount,
    string Reason) : IDomainEvent;

/// <summary>Se aprobó o rechazó una solicitud (o el pago se anuló directo): el aviso queda atendido.</summary>
/// <param name="RequestedBy">Quien pidió la anulación; en un rechazo recibe el aviso con la nota.</param>
public sealed record VoidRequestResolved(
    Guid GymId, Guid VoidRequestId, Guid PaymentId, Guid ResolvedBy, bool Approved,
    Guid? RequestedBy = null, string? RejectionNote = null) : IDomainEvent;
