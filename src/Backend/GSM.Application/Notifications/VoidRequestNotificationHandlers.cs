using System.Globalization;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Identity.Public;
using GSM.Application.Members.Public;
using GSM.Application.Notifications.Abstractions;
using GSM.Application.Payments.Events;
using GSM.Domain.Notifications;

namespace GSM.Application.Notifications;

/// <summary>
/// Una solicitud de anulación de un pago es un aviso de acción: una sola fila por gym (la referencia es la
/// solicitud) que ve el staff y se atiende al aprobarla o rechazarla. Idempotente por la referencia.
/// </summary>
public sealed class VoidRequestedCreatesActionNotice(
    INotificationRepository notifications,
    IIdentityReader identity,
    IMemberReader members,
    IClock clock,
    IUnitOfWork unitOfWork) : IDomainEventHandler<VoidRequested>
{
    private static readonly CultureInfo Colombia = CultureInfo.GetCultureInfo("es-CO");

    public async Task HandleAsync(VoidRequested domainEvent, CancellationToken cancellationToken = default)
    {
        if (await notifications.ExistsAsync(
                NotificationType.VoidRequested, null, null, domainEvent.VoidRequestId, cancellationToken))
        {
            return;
        }

        var who = (await identity.GetUserAsync(domainEvent.RequestedBy, cancellationToken))?.FullName ?? "Alguien del equipo";
        var amount = domainEvent.PaymentAmount.ToString("C0", Colombia);
        var memberPart = string.Empty;
        if (domainEvent.MemberId is { } memberId && await members.GetAsync(memberId, cancellationToken) is { } member)
        {
            memberPart = $" de {member.FirstName} {member.LastName}".TrimEnd();
        }

        var body = $"{who} pide anular «{domainEvent.PaymentConcept}»{memberPart} · {amount}. Motivo: {domainEvent.Reason}";
        if (body.Length > Notification.BodyMaxLength)
        {
            body = body[..Notification.BodyMaxLength];
        }

        notifications.Add(Notification.CreateGymAction(
            domainEvent.GymId, NotificationType.VoidRequested, domainEvent.VoidRequestId,
            "Solicitud de anulación de un pago", body, "/payments", clock.UtcNow));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Al aprobar o rechazar la solicitud (o anular el pago directo), su aviso queda atendido para todos.</summary>
public sealed class VoidRequestResolvedResolvesNotice(
    INotificationRepository notifications,
    IClock clock) : IDomainEventHandler<VoidRequestResolved>
{
    public async Task HandleAsync(VoidRequestResolved domainEvent, CancellationToken cancellationToken = default)
    {
        var notice = await notifications.FindAsync(
            NotificationType.VoidRequested, null, null, domainEvent.VoidRequestId, cancellationToken);
        if (notice is null || notice.ResolvedAt is not null)
        {
            return;
        }

        // El primero gana; si ya estaba atendido no hace nada.
        await notifications.TryResolveAsync(notice.Id, domainEvent.ResolvedBy, clock.UtcNow, cancellationToken);
    }
}

/// <summary>
/// Un rechazo le avisa a quien pidió la anulación, con la nota de quien rechazó. Quien pide no tiene el
/// permiso de ver la bandeja de solicitudes, así que sin esto no se enteraría. Idempotente por la solicitud.
/// </summary>
public sealed class VoidRequestRejectedNotifiesRequester(
    INotificationRepository notifications,
    IIdentityReader identity,
    IClock clock,
    IUnitOfWork unitOfWork) : IDomainEventHandler<VoidRequestResolved>
{
    public async Task HandleAsync(VoidRequestResolved domainEvent, CancellationToken cancellationToken = default)
    {
        if (domainEvent.Approved || domainEvent.RequestedBy is not { } requester
            || await notifications.ExistsAsync(
                NotificationType.VoidRequestRejected, requester, null, domainEvent.VoidRequestId, cancellationToken))
        {
            return;
        }

        var who = (await identity.GetUserAsync(domainEvent.ResolvedBy, cancellationToken))?.FullName ?? "Alguien del equipo";
        var body = string.IsNullOrWhiteSpace(domainEvent.RejectionNote)
            ? $"{who} rechazó tu solicitud de anulación."
            : $"{who} rechazó tu solicitud de anulación: {domainEvent.RejectionNote}";
        if (body.Length > Notification.BodyMaxLength)
        {
            body = body[..Notification.BodyMaxLength];
        }

        notifications.Add(Notification.CreateForUser(
            domainEvent.GymId, requester, NotificationType.VoidRequestRejected, domainEvent.VoidRequestId,
            "Solicitud de anulación rechazada", body, "/payments", clock.UtcNow));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
