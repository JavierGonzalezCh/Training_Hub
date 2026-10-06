using System.Globalization;
using GSM.Application.Achievements.Events;
using GSM.Application.Achievements.Public;
using GSM.Application.Classes.Events;
using GSM.Application.Classes.Public;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Members.Public;
using GSM.Domain.Communications;
using GSM.Domain.Members;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Communications.Events;

/// <summary>Avisa a quienes tenían reserva en una sesión que se canceló: en la app y por email.</summary>
public sealed class ClassSessionCancelledNotifiesMembersHandler(
    IClassReader classes,
    IMemberReader members,
    MemberNotifier notifier,
    IUnitOfWork unitOfWork,
    ILogger<ClassSessionCancelledNotifiesMembersHandler> logger) : IDomainEventHandler<ClassSessionCancelled>
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

            if (await notifier.NotifyAsync(
                    domainEvent.GymId, memberId, NotificationType.ClassCancelled, reference,
                    $"{className} del {day} se canceló",
                    "Tu reserva quedó anulada. Revisa el horario para apuntarte a otra sesión.",
                    "/app/clases", sendEmail: true, cancellationToken))
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

/// <summary>Felicita al miembro por un logro. Solo en la app: un correo por cada logro sería ruido.</summary>
public sealed class AchievementUnlockedNotifiesMemberHandler(
    IAchievementReader achievements,
    MemberNotifier notifier,
    IUnitOfWork unitOfWork) : IDomainEventHandler<AchievementUnlockedEvent>
{
    public async Task HandleAsync(AchievementUnlockedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        var name = await achievements.GetAchievementNameAsync(domainEvent.AchievementId, cancellationToken) ?? "un logro";
        if (await notifier.NotifyAsync(
                domainEvent.GymId, domainEvent.MemberId, NotificationType.AchievementUnlocked,
                domainEvent.MemberAchievementId,
                $"Desbloqueaste «{name}»",
                "Ya aparece en tus logros.",
                "/app/logros", sendEmail: false, cancellationToken))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
