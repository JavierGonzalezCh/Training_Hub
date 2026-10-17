using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Members.Public;
using GSM.Application.Memberships.Events;
using GSM.Domain.Members;
using GSM.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Notifications;

/// <summary>
/// MS5-5: cuando un check-in gasta la última entrada de una tiquetera, el miembro lo sabe al instante y no se entera
/// en la puerta. Categoría Membresía (respeta sus preferencias de bandeja y push), sin correo. Idempotente: la
/// referencia es la membresía, así que reprocesar el evento no duplica el aviso.
/// </summary>
public sealed class PassExhaustedCreatesNotice(
    IMemberReader members,
    MemberNoticeWriter writer,
    IUnitOfWork unitOfWork,
    ILogger<PassExhaustedCreatesNotice> logger) : IDomainEventHandler<PassExhausted>
{
    public async Task HandleAsync(PassExhausted domainEvent, CancellationToken cancellationToken = default)
    {
        var member = await members.GetAsync(domainEvent.MemberId, cancellationToken);
        if (member is null || member.Status != MemberStatus.Active)
        {
            return;
        }

        if (await writer.NotifyAsync(
                new MemberNotice(
                    domainEvent.GymId, domainEvent.MemberId, NotificationType.PassExhausted,
                    NotificationCategory.Membership, domainEvent.MembershipId,
                    "Usaste la última entrada de tu tiquetera",
                    "Renueva en recepción para seguir entrando.",
                    "/app"),
                sendEmail: false, cancellationToken))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Aviso de tiquetera agotada al miembro {MemberId} (membresía {MembershipId})",
                domainEvent.MemberId, domainEvent.MembershipId);
        }
    }
}
