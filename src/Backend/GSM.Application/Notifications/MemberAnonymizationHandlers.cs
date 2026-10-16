using System.Globalization;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Gyms.Public;
using GSM.Application.Identity.Events;
using GSM.Application.Identity.Public;
using GSM.Application.Members.Events;
using GSM.Application.Notifications.Abstractions;
using GSM.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Notifications;

/// <summary>
/// M6, aviso previo a los Owners (Members §11, Notifications §9): faltan pocos días para anonimizar la ficha de un
/// miembro dado de baja. Un aviso informativo por Owner activo, con su correo en la cola. Idempotente por
/// <c>(tipo, Owner, referencia)</c>; la referencia mezcla el miembro y el día previsto, así que una baja posterior a una
/// reactivación avisa de nuevo. El aviso no lleva el nombre del miembro: se conserva y no debe guardar datos personales.
/// </summary>
public sealed class MemberAnonymizationDueNotifiesOwners(
    IIdentityReader identity,
    IGymReader gyms,
    INotificationRepository notifications,
    ScheduledNoticeService scheduler,
    IUnitOfWork unitOfWork,
    ILogger<MemberAnonymizationDueNotifiesOwners> logger) : IDomainEventHandler<MemberAnonymizationDue>
{
    private static readonly CultureInfo Colombia = CultureInfo.GetCultureInfo("es-CO");

    public async Task HandleAsync(MemberAnonymizationDue domainEvent, CancellationToken cancellationToken = default)
    {
        var owners = await identity.ListActiveOwnersAsync(domainEvent.GymId, cancellationToken);
        if (owners.Count == 0)
        {
            return;
        }

        var reference = ReferenceFor(domainEvent.MemberId, domainEvent.AnonymizesOn);
        var date = domainEvent.AnonymizesOn.ToString("d 'de' MMMM", Colombia);
        const string title = "Una ficha se anonimizará pronto";
        var body = $"La ficha de un miembro dado de baja se anonimizará el {date}. Es irreversible. "
            + "Si quieres conservarla, reactívala antes de esa fecha.";
        var link = $"/members/{domainEvent.MemberId}";
        var gymName = (await gyms.GetBrandingAsync(domainEvent.GymId, cancellationToken))?.Name ?? "Tu gimnasio";
        var scheduledFor = scheduler.NextDailySend();

        var created = 0;
        foreach (var owner in owners)
        {
            if (await notifications.ExistsAsync(
                    NotificationType.MemberAnonymizationUpcoming, owner.UserId, null, reference, cancellationToken))
            {
                continue;
            }

            var notification = Notification.CreateForUser(
                domainEvent.GymId, owner.UserId, NotificationType.MemberAnonymizationUpcoming, reference,
                title, body, link, scheduledFor);
            notifications.Add(notification);
            created++;

            if (string.IsNullOrWhiteSpace(owner.Email))
            {
                continue;
            }

            var firstName = owner.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? owner.FullName;
            var (subject, html, text) = NotificationEmails.Compose(gymName, firstName, title, body, null, "Miembros");
            notifications.AddDelivery(NotificationDelivery.CreateEmail(
                domainEvent.GymId, notification.Id, owner.Email, subject, html, text));
        }

        if (created == 0)
        {
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Aviso de anonimización próxima creado para {Count} Owners del gym {GymId}", created, domainEvent.GymId);
    }

    /// <summary>Miembro + día previsto: los últimos 4 bytes del id se sustituyen por el número de día.</summary>
    internal static Guid ReferenceFor(Guid memberId, DateOnly anonymizesOn)
    {
        var bytes = memberId.ToByteArray();
        BitConverter.GetBytes(anonymizesOn.DayNumber).CopyTo(bytes, 12);
        return new Guid(bytes);
    }
}

/// <summary>
/// M6: al anonimizar una ficha se cancelan los avisos del miembro que no habían salido y se vacían sus textos y las
/// direcciones de sus entregas de correo. Idempotente.
/// </summary>
public sealed class ClearMemberNotificationsOnMemberAnonymizedHandler(
    INotificationRepository notifications,
    ILogger<ClearMemberNotificationsOnMemberAnonymizedHandler> logger) : IDomainEventHandler<MemberAnonymized>
{
    public async Task HandleAsync(MemberAnonymized domainEvent, CancellationToken cancellationToken = default)
    {
        await notifications.ClearMemberDataAsync(domainEvent.GymId, domainEvent.MemberId, cancellationToken);
        logger.LogInformation("Avisos del miembro anonimizado {MemberId} limpiados", domainEvent.MemberId);
    }
}

/// <summary>Se eliminó una cuenta: sus dispositivos de push ya no sirven. Idempotente.</summary>
public sealed class DeletePushDevicesOnUserAccountDeletedHandler(
    IPushDeviceRepository devices,
    ILogger<DeletePushDevicesOnUserAccountDeletedHandler> logger) : IDomainEventHandler<UserAccountDeleted>
{
    public async Task HandleAsync(UserAccountDeleted domainEvent, CancellationToken cancellationToken = default)
    {
        await devices.DeleteForUserAsync(domainEvent.UserId, cancellationToken);
        logger.LogInformation("Dispositivos de push de la cuenta eliminada {UserId} borrados", domainEvent.UserId);
    }
}
