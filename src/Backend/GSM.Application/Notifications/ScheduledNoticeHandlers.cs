using System.Globalization;
using GSM.Application.Classes.Events;
using GSM.Application.Classes.Public;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Communications.Events;
using GSM.Application.Members.Public;
using GSM.Domain.Members;
using GSM.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Notifications;

/// <summary>
/// N5, avisos programados y cancelables (spec Notifications §4.4). Todos los manejadores son idempotentes: la
/// clave <c>(tipo, destinatario, referencia)</c> evita duplicar si el evento se repite, y programar de nuevo
/// un aviso cancelado lo reactiva. La referencia de los recordatorios de clase es la ocurrencia (clase + día),
/// la misma que usa el aviso de clase cancelada.
/// </summary>

/// <summary>
/// Al confirmar la reserva programa el recordatorio de 10 minutos antes del inicio, y el del instructor de la
/// sesión. Si la reserva se hizo dentro de esos 10 minutos no hay recordatorio que dar.
/// </summary>
public sealed class ClassBookedSchedulesReminders(
    IClassReader classes,
    IMemberReader members,
    ScheduledNoticeService scheduler,
    IClock clock,
    IUnitOfWork unitOfWork,
    ILogger<ClassBookedSchedulesReminders> logger) : IDomainEventHandler<ClassBooked>
{
    private static readonly CultureInfo Colombia = CultureInfo.GetCultureInfo("es-CO");

    public async Task HandleAsync(ClassBooked domainEvent, CancellationToken cancellationToken = default)
    {
        var remindAt = scheduler.At(domainEvent.SessionDate, domainEvent.StartTime)
            - ScheduledNoticeService.ClassReminderLead;
        if (remindAt <= clock.UtcNow)
        {
            return;
        }

        var member = await members.GetAsync(domainEvent.MemberId, cancellationToken);
        if (member is null || member.Status != MemberStatus.Active)
        {
            return;
        }

        var className = await classes.GetClassNameAsync(domainEvent.ClassId, cancellationToken) ?? "Tu clase";
        var time = domainEvent.StartTime.ToString("h:mm tt", Colombia);
        var reference = ClassSessionCancelledCreatesNotices.SessionReference(domainEvent.ClassId, domainEvent.SessionDate);

        await scheduler.ScheduleForMemberAsync(
            new MemberNotice(
                domainEvent.GymId, domainEvent.MemberId, NotificationType.ClassReminder, NotificationCategory.Classes,
                reference, $"{className} empieza en 10 minutos", $"Tu clase es hoy a las {time}.", "/app/clases",
                remindAt),
            sendEmail: false, cancellationToken);

        if (domainEvent.InstructorUserId is { } instructorId)
        {
            await scheduler.ScheduleForUserAsync(
                domainEvent.GymId, instructorId, NotificationType.InstructorClassReminder, reference,
                $"{className} empieza en 10 minutos", $"Das la clase hoy a las {time}.", "/classes/mine",
                remindAt, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Recordatorio de clase programado: miembro {MemberId}, clase {ClassId} del {Date} a las {RemindAt}",
            domainEvent.MemberId, domainEvent.ClassId, domainEvent.SessionDate, remindAt);
    }
}

/// <summary>Si el miembro cancela su reserva, el recordatorio programado deja de salir.</summary>
public sealed class ClassBookingCancelledCancelsReminder(
    ScheduledNoticeService scheduler,
    IUnitOfWork unitOfWork) : IDomainEventHandler<ClassBookingCancelled>
{
    public async Task HandleAsync(ClassBookingCancelled domainEvent, CancellationToken cancellationToken = default)
    {
        var reference = ClassSessionCancelledCreatesNotices.SessionReference(domainEvent.ClassId, domainEvent.SessionDate);
        if (await scheduler.CancelForMemberAsync(
                NotificationType.ClassReminder, domainEvent.MemberId, reference, cancellationToken))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}

/// <summary>
/// Si se cancela la sesión, dejan de salir los recordatorios de todos los reservados y del instructor. El
/// aviso de "clase cancelada" lo crea otro manejador del mismo evento.
/// </summary>
public sealed class ClassSessionCancelledCancelsReminders(
    ScheduledNoticeService scheduler,
    IUnitOfWork unitOfWork,
    ILogger<ClassSessionCancelledCancelsReminders> logger) : IDomainEventHandler<ClassSessionCancelled>
{
    private static readonly NotificationType[] ReminderTypes =
        [NotificationType.ClassReminder, NotificationType.InstructorClassReminder];

    public async Task HandleAsync(ClassSessionCancelled domainEvent, CancellationToken cancellationToken = default)
    {
        var reference = ClassSessionCancelledCreatesNotices.SessionReference(domainEvent.ClassId, domainEvent.SessionDate);
        var cancelled = await scheduler.CancelAllByReferenceAsync(ReminderTypes, reference, cancellationToken);
        if (cancelled == 0)
        {
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Clase {ClassId} del {Date} cancelada: {Count} recordatorios cancelados",
            domainEvent.ClassId, domainEvent.SessionDate, cancelled);
    }
}

/// <summary>
/// Comunicaciones (CO5) avisa de que toca felicitar a un miembro; aquí se programa para su hora (las 8:00) como
/// aviso de Novedades. Solo a miembros activos.
/// </summary>
public sealed class BirthdayGreetingDueSchedulesNotice(
    IMemberReader members,
    ScheduledNoticeService scheduler,
    IUnitOfWork unitOfWork) : IDomainEventHandler<BirthdayGreetingDue>
{
    public async Task HandleAsync(BirthdayGreetingDue domainEvent, CancellationToken cancellationToken = default)
    {
        var member = await members.GetAsync(domainEvent.MemberId, cancellationToken);
        if (member is null || member.Status != MemberStatus.Active)
        {
            return;
        }

        // Una felicitación por miembro y fecha de cumpleaños: repetir el evento no la duplica.
        var reference = ReferenceFor(domainEvent.MemberId, domainEvent.Date);
        var title = domainEvent.Title.Length > Notification.TitleMaxLength
            ? domainEvent.Title[..Notification.TitleMaxLength]
            : domainEvent.Title;

        await scheduler.ScheduleForMemberAsync(
            new MemberNotice(
                domainEvent.GymId, domainEvent.MemberId, NotificationType.Birthday, NotificationCategory.News,
                reference, title, domainEvent.Body, "/app", domainEvent.SendAt),
            sendEmail: false, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Id estable derivado del miembro y el día del cumpleaños.</summary>
    internal static Guid ReferenceFor(Guid memberId, DateOnly date)
        => new(System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{memberId:N}:birthday:{date:yyyy-MM-dd}")));
}

/// <summary>C7: si el miembro ya marcó "Estoy en clase", el recordatorio programado no tiene sentido. Idempotente.</summary>
public sealed class ClassSelfAttendedCancelsReminder(
    ScheduledNoticeService scheduler,
    IUnitOfWork unitOfWork) : IDomainEventHandler<ClassSelfAttended>
{
    public async Task HandleAsync(ClassSelfAttended domainEvent, CancellationToken cancellationToken = default)
    {
        var reference = ClassSessionCancelledCreatesNotices.SessionReference(domainEvent.ClassId, domainEvent.SessionDate);
        if (await scheduler.CancelForMemberAsync(
                NotificationType.ClassReminder, domainEvent.MemberId, reference, cancellationToken))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
