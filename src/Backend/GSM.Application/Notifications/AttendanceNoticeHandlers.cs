using GSM.Application.Attendance.Events;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Domain.Members;
using GSM.Domain.Notifications;
using GSM.Application.Members.Public;

namespace GSM.Application.Notifications;

/// <summary>
/// A7: "Se registró tu entrada con tu documento a las 6:12". Aviso de seguridad al miembro: bandeja, push y
/// correo, sin preferencia y sin horario sin avisos. Idempotente por <c>(tipo, miembro, asistencia)</c>.
/// </summary>
public sealed class DocumentCheckInRegisteredCreatesNotice(
    IMemberReader members,
    MemberNoticeWriter writer,
    IClock clock,
    IUnitOfWork unitOfWork) : IDomainEventHandler<DocumentCheckInRegistered>
{
    public async Task HandleAsync(DocumentCheckInRegistered domainEvent, CancellationToken cancellationToken = default)
    {
        var member = await members.GetAsync(domainEvent.MemberId, cancellationToken);
        if (member is null || member.Status != MemberStatus.Active)
        {
            return;
        }

        var at = clock.ToBusinessLocal(domainEvent.CheckedInAt).ToString("H:mm");
        if (await writer.NotifyAsync(
                new MemberNotice(
                    domainEvent.GymId, domainEvent.MemberId, NotificationType.DocumentCheckIn,
                    NotificationCategory.Security, domainEvent.AttendanceId,
                    $"Se registró tu entrada con tu documento a las {at}",
                    "Si no fuiste tú, avisa en recepción de inmediato.",
                    "/app"),
                sendEmail: true, cancellationToken))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
