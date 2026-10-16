using System.Globalization;
using GSM.Application.Classes.Events;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Members.Public;
using GSM.Domain.Members;
using GSM.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Notifications;

/// <summary>
/// C3-5: avisa al miembro cuando queda penalizado por inasistencias ("no podrás reservar hasta el X") y cuando el staff
/// levanta la penalización. Antes se enteraba solo al intentar reservar. Categoría Clases (respeta sus preferencias),
/// sin correo. Idempotente por la referencia (miembro + fecha), como todo manejador de Notifications.
/// </summary>
public sealed class BookingPenaltyAppliedCreatesNotice(
    IMemberReader members,
    MemberNoticeWriter writer,
    IUnitOfWork unitOfWork,
    ILogger<BookingPenaltyAppliedCreatesNotice> logger) : IDomainEventHandler<BookingPenaltyApplied>
{
    private static readonly CultureInfo Colombia = CultureInfo.GetCultureInfo("es-CO");

    public async Task HandleAsync(BookingPenaltyApplied domainEvent, CancellationToken cancellationToken = default)
    {
        var member = await members.GetAsync(domainEvent.MemberId, cancellationToken);
        if (member is null || member.Status != MemberStatus.Active)
        {
            return;
        }

        var until = domainEvent.BlockedUntil.ToString("dddd d 'de' MMMM", Colombia);
        if (await writer.NotifyAsync(
                new MemberNotice(
                    domainEvent.GymId, domainEvent.MemberId, NotificationType.BookingPenaltyApplied,
                    NotificationCategory.Classes,
                    BookingPenaltyReference.For(domainEvent.MemberId, domainEvent.BlockedUntil),
                    "No puedes reservar clases por ahora",
                    $"Faltaste a varias clases reservadas sin cancelar a tiempo. Podrás volver a reservar después del {until}.",
                    "/app/clases"),
                sendEmail: false, cancellationToken))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Aviso de penalización de reservas al miembro {MemberId} (hasta {Until})",
                domainEvent.MemberId, domainEvent.BlockedUntil);
        }
    }
}

public sealed class BookingPenaltyLiftedCreatesNotice(
    IClock clock,
    IMemberReader members,
    MemberNoticeWriter writer,
    IUnitOfWork unitOfWork,
    ILogger<BookingPenaltyLiftedCreatesNotice> logger) : IDomainEventHandler<BookingPenaltyLifted>
{
    public async Task HandleAsync(BookingPenaltyLifted domainEvent, CancellationToken cancellationToken = default)
    {
        var member = await members.GetAsync(domainEvent.MemberId, cancellationToken);
        if (member is null || member.Status != MemberStatus.Active)
        {
            return;
        }

        if (await writer.NotifyAsync(
                new MemberNotice(
                    domainEvent.GymId, domainEvent.MemberId, NotificationType.BookingPenaltyLifted,
                    NotificationCategory.Classes,
                    BookingPenaltyReference.For(domainEvent.MemberId, clock.Today),
                    "Ya puedes reservar clases",
                    "El gimnasio levantó tu bloqueo de reservas.",
                    "/app/clases"),
                sendEmail: false, cancellationToken))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Aviso de penalización levantada al miembro {MemberId}", domainEvent.MemberId);
        }
    }
}

internal static class BookingPenaltyReference
{
    /// <summary>Guid estable a partir del miembro y una fecha: el evento no trae un id propio.</summary>
    public static Guid For(Guid memberId, DateOnly date)
    {
        var bytes = memberId.ToByteArray();
        var dayBytes = BitConverter.GetBytes(date.DayNumber);
        for (var i = 0; i < dayBytes.Length; i++)
        {
            bytes[i] ^= dayBytes[i];
        }

        return new Guid(bytes);
    }
}
