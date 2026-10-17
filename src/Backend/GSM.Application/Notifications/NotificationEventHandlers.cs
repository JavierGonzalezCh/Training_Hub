using System.Globalization;
using GSM.Application.Achievements.Events;
using GSM.Application.Achievements.Public;
using GSM.Application.Attendance.Events;
using GSM.Application.Classes.Events;
using GSM.Application.Classes.Public;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Communications.Events;
using GSM.Application.Members.Public;
using GSM.Application.Notifications.Abstractions;
using GSM.Domain.Attendance;
using GSM.Domain.Communications;
using GSM.Domain.Members;
using GSM.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Notifications;

/// <summary>
/// Los manejadores de eventos que crean avisos (spec §4.3). Todos son idempotentes: el índice único
/// <c>(tipo, destinatario, referencia)</c> y una consulta previa evitan duplicar si el evento se repite.
/// </summary>

/// <summary>Avisa a quienes tenían reserva en una sesión que se canceló: en la bandeja y por email.</summary>
public sealed class ClassSessionCancelledCreatesNotices(
    IClassReader classes,
    IMemberReader members,
    MemberNoticeWriter writer,
    IUnitOfWork unitOfWork,
    ILogger<ClassSessionCancelledCreatesNotices> logger) : IDomainEventHandler<ClassSessionCancelled>
{
    private static readonly CultureInfo Colombia = CultureInfo.GetCultureInfo("es-CO");

    public async Task HandleAsync(ClassSessionCancelled domainEvent, CancellationToken cancellationToken = default)
    {
        if (domainEvent.AffectedMemberIds.Count == 0)
        {
            return;
        }

        var className = await classes.GetClassNameAsync(domainEvent.ClassId, cancellationToken) ?? "Tu clase";
        var day = domainEvent.SessionDate.ToString("dddd d 'de' MMMM", Colombia);

        // La referencia es la ocurrencia (clase + día): la misma clase cancelada otro día es otro aviso.
        var reference = SessionReference(domainEvent.ClassId, domainEvent.SessionDate);
        var created = 0;
        foreach (var memberId in domainEvent.AffectedMemberIds.Distinct())
        {
            var member = await members.GetAsync(memberId, cancellationToken);
            if (member is null || member.Status != MemberStatus.Active)
            {
                continue;
            }

            if (await writer.NotifyAsync(
                    new MemberNotice(
                        domainEvent.GymId, memberId, NotificationType.ClassCancelled, NotificationCategory.Classes,
                        reference,
                        $"{className} del {day} se canceló",
                        "Tu reserva quedó anulada. Revisa el horario para apuntarte a otra sesión.",
                        "/app/clases"),
                    sendEmail: true, cancellationToken))
            {
                created++;
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Clase {ClassId} del {Date} cancelada: {Count} avisos creados",
            domainEvent.ClassId, domainEvent.SessionDate, created);
    }

    /// <summary>Guid estable a partir de la clase y el día: el evento no trae el id de la sesión.</summary>
    internal static Guid SessionReference(Guid classId, DateOnly date)
    {
        var bytes = classId.ToByteArray();
        var dayBytes = BitConverter.GetBytes(date.DayNumber);
        for (var i = 0; i < dayBytes.Length; i++)
        {
            bytes[i] ^= dayBytes[i];
        }

        return new Guid(bytes);
    }
}

/// <summary>Felicita al miembro por un logro. Solo en la bandeja: un correo por cada logro sería ruido.</summary>
public sealed class AchievementUnlockedCreatesNotice(
    IAchievementReader achievements,
    MemberNoticeWriter writer,
    IUnitOfWork unitOfWork) : IDomainEventHandler<AchievementUnlockedEvent>
{
    public async Task HandleAsync(AchievementUnlockedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        var name = await achievements.GetAchievementNameAsync(domainEvent.AchievementId, cancellationToken) ?? "un logro";
        if (await writer.NotifyAsync(
                new MemberNotice(
                    domainEvent.GymId, domainEvent.MemberId, NotificationType.AchievementUnlocked,
                    NotificationCategory.Progress, domainEvent.MemberAchievementId,
                    $"Desbloqueaste «{name}»", "Ya aparece en tus logros.", "/app/logros"),
                sendEmail: false, cancellationToken))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}

/// <summary>Un aviso por miembro de la audiencia del anuncio y, si el anuncio lo pide, su correo.</summary>
public sealed class AnnouncementPublishedCreatesNotices(
    MemberNoticeWriter writer,
    IUnitOfWork unitOfWork,
    ILogger<AnnouncementPublishedCreatesNotices> logger) : IDomainEventHandler<AnnouncementPublished>
{
    public async Task HandleAsync(AnnouncementPublished domainEvent, CancellationToken cancellationToken = default)
    {
        var (notices, emails) = await writer.NotifyManyAsync(
            domainEvent.GymId,
            domainEvent.MemberIds,
            domainEvent.Important ? NotificationType.ImportantAnnouncement : NotificationType.Announcement,
            domainEvent.Category == AnnouncementCategory.Promotions
                ? NotificationCategory.Promotions
                : NotificationCategory.News,
            domainEvent.AnnouncementId,
            domainEvent.Title,
            domainEvent.Body,
            "/app",
            domainEvent.SendEmail ? domainEvent.Body : null,
            cancellationToken,
            bypassPreferences: domainEvent.Important);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Anuncio {AnnouncementId}: {Notices} avisos y {Emails} correos creados",
            domainEvent.AnnouncementId, notices, emails);
    }
}

/// <summary>Recordatorio de que la membresía vence pronto: bandeja y correo.</summary>
public sealed class MembershipReminderDueCreatesNotice(
    MemberNoticeWriter writer,
    ScheduledNoticeService scheduler,
    IMemberReader members,
    IUnitOfWork unitOfWork) : IDomainEventHandler<MembershipReminderDue>
{
    private static readonly CultureInfo Colombia = CultureInfo.GetCultureInfo("es-CO");

    public async Task HandleAsync(MembershipReminderDue domainEvent, CancellationToken cancellationToken = default)
    {
        var contact = await members.GetContactAsync(domainEvent.MemberId, cancellationToken);
        if (contact is null || contact.Status != MemberStatus.Active)
        {
            return;
        }

        var renewBody = "Pasa por recepción para renovarlo y seguir entrenando sin interrupciones.";
        var when = domainEvent.DaysLeft == 1
            ? "mañana"
            : $"el {domainEvent.EndDate.ToString("dddd d 'de' MMMM", Colombia)}";
        var (title, body, referenceId) = domainEvent.Kind switch
        {
            MembershipReminderKind.ExpiresToday => (
                $"Tu plan {domainEvent.PlanName} vence hoy", renewBody,
                MembershipReminderReferences.For(domainEvent.MembershipId, MembershipReminderReferences.ExpiresToday)),
            MembershipReminderKind.LastGraceDay => (
                "Hoy es tu último día para renovar sin perder tu fecha",
                $"Tu plan {domainEvent.PlanName} venció el {domainEvent.EndDate.ToString("d 'de' MMMM", Colombia)}. "
                    + "Renueva hoy en recepción para conservar tu fecha de renovación.",
                MembershipReminderReferences.For(domainEvent.MembershipId, MembershipReminderReferences.LastGraceDay)),
            _ => ($"Tu plan {domainEvent.PlanName} vence {when}", renewBody, domainEvent.MembershipId),
        };

        if (await writer.NotifyAsync(
                new MemberNotice(
                    domainEvent.GymId, domainEvent.MemberId, NotificationType.MembershipExpiring,
                    NotificationCategory.Membership, referenceId, title, body, "/app",
                    // N5: el horario de envío de los vencimientos son las 8:00 del gym (spec §9). La tarea de
                    // Comunicaciones sigue generándolos cuando toca; aquí solo se difiere su salida.
                    scheduler.NextDailySend()),
                sendEmail: true, cancellationToken))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}

/// <summary>
/// Un intento de entrada rechazado es un aviso de acción: una sola fila por gym (la referencia es el
/// intento), que ve todo el staff y se atiende para todos. Si el miembro insiste mientras sigue sin
/// atender, el mismo aviso actualiza su contador.
/// </summary>
public sealed class CheckInRejectedCreatesActionNotice(
    INotificationRepository notifications,
    IMemberReader members,
    IClock clock,
    IUnitOfWork unitOfWork) : IDomainEventHandler<CheckInRejected>
{
    private static readonly CultureInfo Colombia = CultureInfo.GetCultureInfo("es-CO");

    public async Task HandleAsync(CheckInRejected domainEvent, CancellationToken cancellationToken = default)
    {
        var member = await members.GetAsync(domainEvent.MemberId, cancellationToken);
        var name = member is null ? "Un miembro" : $"{member.FirstName} {member.LastName}".Trim();

        var reason = domainEvent.Reason switch
        {
            CheckinRejectionReason.NoMembership => "Sin membresía",
            CheckinRejectionReason.MembershipFrozen => "Membresía congelada",
            CheckinRejectionReason.PassExhausted => "Sin entradas en la tiquetera",
            CheckinRejectionReason.MemberInactive => "Miembro dado de baja",
            _ => domainEvent.MembershipEndedOn is { } ended
                ? $"Venció el {ended.ToString("d 'de' MMMM", Colombia)}"
                : "Membresía vencida",
        };
        var body = domainEvent.AttemptCount > 1
            ? $"{name} · {reason} · {domainEvent.AttemptCount} intentos"
            : $"{name} · {reason}";

        var existing = await notifications.FindAsync(
            NotificationType.CheckinRejected, null, null, domainEvent.AttemptId, cancellationToken);
        if (existing is not null)
        {
            existing.UpdateText(existing.Title, body);
        }
        else
        {
            notifications.Add(Notification.CreateGymAction(
                domainEvent.GymId, NotificationType.CheckinRejected, domainEvent.AttemptId,
                "Entrada rechazada", body, $"/members/{domainEvent.MemberId}", clock.UtcNow));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
