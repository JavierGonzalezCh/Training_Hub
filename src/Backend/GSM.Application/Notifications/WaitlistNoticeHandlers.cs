using System.Globalization;
using GSM.Application.Classes.Events;
using GSM.Application.Classes.Public;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Members.Public;
using GSM.Domain.Members;
using GSM.Domain.Notifications;

namespace GSM.Application.Notifications;

/// <summary>
/// C4, lista de espera de clases. Los dos manejadores son idempotentes: la referencia del aviso es la entrada de la
/// lista, así que repetir el evento no duplica el aviso. Solo bandeja y push (el correo llegaría tarde para una
/// oferta de una hora, spec Notifications §9).
/// </summary>

/// <summary>Se liberó un cupo: aviso urgente al primero de la lista, con la hora límite para confirmar.</summary>
public sealed class WaitlistOfferMadeCreatesNotice(
    IClassReader classes,
    IMemberReader members,
    MemberNoticeWriter writer,
    IClock clock,
    IUnitOfWork unitOfWork) : IDomainEventHandler<WaitlistOfferMade>
{
    private static readonly CultureInfo Colombia = CultureInfo.GetCultureInfo("es-CO");

    public async Task HandleAsync(WaitlistOfferMade domainEvent, CancellationToken cancellationToken = default)
    {
        var member = await members.GetAsync(domainEvent.MemberId, cancellationToken);
        if (member is null || member.Status != MemberStatus.Active)
        {
            return;
        }

        var className = await classes.GetClassNameAsync(domainEvent.ClassId, cancellationToken) ?? "tu clase";
        var day = domainEvent.SessionDate.ToString("dddd d 'de' MMMM", Colombia);
        var start = domainEvent.StartTime.ToString("h:mm tt", Colombia);
        var deadline = TimeOnly.FromDateTime(clock.ToBusinessLocal(domainEvent.OfferExpiresAt)).ToString("h:mm tt", Colombia);

        if (await writer.NotifyAsync(
                new MemberNotice(
                    domainEvent.GymId, domainEvent.MemberId, NotificationType.WaitlistOffer, NotificationCategory.Classes,
                    domainEvent.EntryId,
                    $"Se liberó un cupo en {className}",
                    $"{className} del {day} a las {start}. Confirma antes de las {deadline} o el cupo pasa al siguiente.",
                    "/app/clases",
                    Priority: NotificationPriority.Urgent),
                sendEmail: false, cancellationToken))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}

/// <summary>La oferta venció sin respuesta: aviso informativo al miembro, que sale de la lista.</summary>
public sealed class WaitlistOfferExpiredCreatesNotice(
    IClassReader classes,
    IMemberReader members,
    MemberNoticeWriter writer,
    IUnitOfWork unitOfWork) : IDomainEventHandler<WaitlistOfferExpired>
{
    private static readonly CultureInfo Colombia = CultureInfo.GetCultureInfo("es-CO");

    public async Task HandleAsync(WaitlistOfferExpired domainEvent, CancellationToken cancellationToken = default)
    {
        var member = await members.GetAsync(domainEvent.MemberId, cancellationToken);
        if (member is null || member.Status != MemberStatus.Active)
        {
            return;
        }

        var className = await classes.GetClassNameAsync(domainEvent.ClassId, cancellationToken) ?? "la clase";
        var day = domainEvent.SessionDate.ToString("dddd d 'de' MMMM", Colombia);

        if (await writer.NotifyAsync(
                new MemberNotice(
                    domainEvent.GymId, domainEvent.MemberId, NotificationType.WaitlistOfferExpired, NotificationCategory.Classes,
                    domainEvent.EntryId,
                    "No confirmaste a tiempo",
                    $"El cupo de {className} del {day} pasó al siguiente de la lista.",
                    "/app/clases"),
                sendEmail: false, cancellationToken))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
