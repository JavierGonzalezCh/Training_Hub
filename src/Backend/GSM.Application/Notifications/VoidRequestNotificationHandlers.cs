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
