using System.Globalization;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Gyms.Public;
using GSM.Application.Identity.Public;
using GSM.Application.Notifications.Abstractions;
using GSM.Application.SuperAdmin.Events;
using GSM.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Notifications;

/// <summary>
/// S3, avisos de la suscripción del gym al SaaS (SuperAdmin §V3, Notifications §9). SuperAdmin publica
/// <see cref="SubscriptionNoticeDue"/>; aquí se crea un aviso informativo por cada Owner activo del gym, con su correo
/// en la cola y su push (lo planea el dispatcher al salir). Idempotente por <c>(tipo, Owner, referencia)</c>: la tarea
/// periódica repite el evento cada pocos minutos y un reintento del manejador tampoco duplica. Si un Owner se suma
/// después, un nuevo evento le llega solo a él.
/// <para>
/// Salen a las 8:00 del gym (o ya, si pasaron) los avisos de calendario; salen de inmediato los hechos (suspensión,
/// reactivación, pago registrado) y la inhabilitación. Suspensión e inhabilitación son urgentes. El horario sin avisos
/// del gym no aplica: es staff.
/// </para>
/// </summary>
public sealed class SubscriptionNoticeDueNotifiesOwners(
    IIdentityReader identity,
    IGymReader gyms,
    INotificationRepository notifications,
    ScheduledNoticeService scheduler,
    IClock clock,
    IUnitOfWork unitOfWork,
    ILogger<SubscriptionNoticeDueNotifiesOwners> logger) : IDomainEventHandler<SubscriptionNoticeDue>
{
    private static readonly CultureInfo Colombia = CultureInfo.GetCultureInfo("es-CO");

    public async Task HandleAsync(SubscriptionNoticeDue domainEvent, CancellationToken cancellationToken = default)
    {
        var owners = await identity.ListActiveOwnersAsync(domainEvent.GymId, cancellationToken);
        if (owners.Count == 0)
        {
            return;
        }

        var type = TypeOf(domainEvent.Kind);
        var (title, body) = Text(domainEvent);
        var urgent = domainEvent.Kind is SubscriptionNoticeKind.Suspended or SubscriptionNoticeKind.Disabled;
        var scheduledFor = IsCalendarNotice(domainEvent.Kind) ? scheduler.NextDailySend() : clock.UtcNow;
        var gymName = (await gyms.GetBrandingAsync(domainEvent.GymId, cancellationToken))?.Name ?? "Tu gimnasio";

        var created = 0;
        foreach (var owner in owners)
        {
            if (await notifications.ExistsAsync(type, owner.UserId, null, domainEvent.ReferenceId, cancellationToken))
            {
                continue;
            }

            var notification = Notification.CreateForUser(
                domainEvent.GymId, owner.UserId, type, domainEvent.ReferenceId, title, body, "/", scheduledFor,
                urgent ? NotificationPriority.Urgent : NotificationPriority.Normal);
            notifications.Add(notification);
            created++;

            if (string.IsNullOrWhiteSpace(owner.Email))
            {
                continue;
            }

            var firstName = owner.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? owner.FullName;
            var (subject, html, text) = NotificationEmails.Compose(gymName, firstName, title, body, null, "Suscripción");
            notifications.AddDelivery(NotificationDelivery.CreateEmail(
                domainEvent.GymId, notification.Id, owner.Email, subject, html, text));
        }

        if (created == 0)
        {
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Aviso de suscripción {Kind} creado para {Count} Owners del gym {GymId}",
            domainEvent.Kind, created, domainEvent.GymId);
    }

    /// <summary>Los hitos de calendario salen a las 8:00; los hechos y la inhabilitación, ya.</summary>
    private static bool IsCalendarNotice(SubscriptionNoticeKind kind) => kind is
        SubscriptionNoticeKind.TrialEnding or SubscriptionNoticeKind.TrialEnded or SubscriptionNoticeKind.Expiring
        or SubscriptionNoticeKind.ExpiresToday or SubscriptionNoticeKind.Overdue or SubscriptionNoticeKind.DisableSoon
        or SubscriptionNoticeKind.MemberLimitNear or SubscriptionNoticeKind.MemberLimitReached;

    internal static NotificationType TypeOf(SubscriptionNoticeKind kind) => kind switch
    {
        SubscriptionNoticeKind.TrialEnding => NotificationType.SubscriptionTrialEnding,
        SubscriptionNoticeKind.TrialEnded => NotificationType.SubscriptionTrialEnded,
        SubscriptionNoticeKind.Expiring => NotificationType.SubscriptionExpiring,
        SubscriptionNoticeKind.ExpiresToday => NotificationType.SubscriptionExpiresToday,
        SubscriptionNoticeKind.Overdue => NotificationType.SubscriptionOverdue,
        SubscriptionNoticeKind.DisableSoon => NotificationType.SubscriptionDisableSoon,
        SubscriptionNoticeKind.Disabled => NotificationType.SubscriptionDisabled,
        SubscriptionNoticeKind.Suspended => NotificationType.SubscriptionSuspended,
        SubscriptionNoticeKind.Reactivated => NotificationType.SubscriptionReactivated,
        SubscriptionNoticeKind.PaymentRegistered => NotificationType.SubscriptionPaymentRegistered,
        SubscriptionNoticeKind.MemberLimitNear => NotificationType.MemberLimitNear,
        SubscriptionNoticeKind.MemberLimitReached => NotificationType.MemberLimitReached,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Aviso de suscripción desconocido."),
    };

    internal static (string Title, string Body) Text(SubscriptionNoticeDue e)
    {
        var date = e.Date is { } d ? Day(d) : string.Empty;
        return e.Kind switch
        {
            SubscriptionNoticeKind.TrialEnding => (
                "Tu prueba termina pronto",
                $"Tu prueba gratis de GSM termina {When(e.Days, date)}. Confirma tu pago para seguir usando GSM sin interrupciones."),
            SubscriptionNoticeKind.TrialEnded => (
                "Tu prueba terminó",
                $"Tienes hasta el {date} para confirmar tu pago. Pasada esa fecha, tu gimnasio quedará solo en modo consulta."),
            SubscriptionNoticeKind.Expiring => (
                "Tu suscripción vence pronto",
                $"Tu suscripción de GSM vence {When(e.Days, date)}. Renueva a tiempo para no perder el acceso completo."),
            SubscriptionNoticeKind.ExpiresToday => (
                "Tu suscripción vence hoy",
                "Hoy es el último día cubierto de tu suscripción de GSM. Renueva para no perder el acceso completo."),
            SubscriptionNoticeKind.Overdue => (
                "Tu gimnasio entró en mora",
                $"Tu suscripción de GSM venció. Tienes hasta el {date} para pagar; después tu gimnasio quedará solo en modo consulta."),
            SubscriptionNoticeKind.DisableSoon => (
                "Tu gimnasio se inhabilitará pronto",
                $"Si no confirmas tu pago antes del {date}, tu gimnasio quedará solo en modo consulta."),
            SubscriptionNoticeKind.Disabled => (
                "Tu gimnasio fue inhabilitado",
                "Por falta de pago, tu gimnasio quedó solo en modo consulta. Escribe a GSM para reactivarlo."),
            SubscriptionNoticeKind.Suspended => (
                "Tu gimnasio fue suspendido",
                "GSM suspendió el acceso de tu gimnasio. Escríbenos para resolverlo."),
            SubscriptionNoticeKind.Reactivated => (
                "Tu gimnasio fue reactivado",
                "Ya puedes volver a usar GSM con normalidad."),
            SubscriptionNoticeKind.PaymentRegistered => (
                "Pago registrado",
                $"Recibimos tu pago de {Money(e.Amount)}; tu suscripción va hasta el {date}."),
            SubscriptionNoticeKind.MemberLimitNear => (
                e.Limit - e.Used == 1 ? "Te queda 1 cupo" : $"Te quedan {e.Limit - e.Used} cupos",
                $"Tienes {e.Used} de {e.Limit} miembros activos de tu plan. Habla con GSM para ampliarlo."),
            SubscriptionNoticeKind.MemberLimitReached => (
                "Llegaste al límite de miembros",
                $"Llegaste al límite de {e.Limit} miembros activos de tu plan. Habla con GSM para ampliarlo. "
                + "Renovar a quien ya está activo no se bloquea."),
            _ => throw new ArgumentOutOfRangeException(nameof(e), e.Kind, "Aviso de suscripción desconocido."),
        };
    }

    private static string Day(DateOnly day) => day.ToString("d 'de' MMMM", Colombia);

    private static string When(int? days, string date) => days switch
    {
        0 => "hoy",
        1 => $"mañana ({date})",
        { } n => $"en {n} días ({date})",
        _ => $"el {date}",
    };

    private static string Money(decimal? amount)
        => amount is { } a ? "$" + a.ToString("N0", Colombia) : "tu pago";
}
