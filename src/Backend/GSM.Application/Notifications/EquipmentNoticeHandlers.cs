using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Equipment.Events;
using GSM.Application.Gyms.Public;
using GSM.Application.Identity.Public;
using GSM.Application.Members.Public;
using GSM.Application.Notifications.Abstractions;
using GSM.Domain.Equipment;
using GSM.Domain.Members;
using GSM.Domain.Notifications;

namespace GSM.Application.Notifications;

/// <summary>
/// E4, reportes de falla de máquinas (Equipment §7). Todos los manejadores son idempotentes: la referencia del aviso
/// es el reporte, y los que cierran o atienden solo actúan sobre avisos que siguen sin atender.
/// </summary>
internal static class EquipmentNoticeText
{
    public static string Reason(EquipmentIssueReason reason) => reason switch
    {
        EquipmentIssueReason.NotWorking => "no funciona",
        EquipmentIssueReason.DamagedPart => "pieza dañada",
        EquipmentIssueReason.StrangeNoise => "ruido raro",
        EquipmentIssueReason.MissingPart => "falta una pieza",
        EquipmentIssueReason.Dirty => "sucia",
        EquipmentIssueReason.Unsafe => "insegura",
        _ => "otro problema",
    };

    public static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..(max - 1)] + "…";
}

/// <summary>
/// Un reporte nuevo es un aviso de acción para todo el staff, uno por reporte. Mientras la máquina tenga otro aviso
/// sin atender, el reporte se suma a ese ("3 reportes de la Prensa 45°"). Un reporte «insegura» nunca se agrupa: sale
/// urgente (primero y en rojo) y se encola un correo para cada Owner por la cola de Notifications.
/// </summary>
public sealed class EquipmentIssueReportedCreatesNotice(
    INotificationRepository notifications,
    IIdentityReader identity,
    IGymReader gyms,
    IClock clock,
    IUnitOfWork unitOfWork) : IDomainEventHandler<EquipmentIssueReported>
{
    public async Task HandleAsync(EquipmentIssueReported domainEvent, CancellationToken cancellationToken = default)
    {
        if (await notifications.ExistsAsync(
                NotificationType.EquipmentIssueReported, null, null, domainEvent.IssueId, cancellationToken))
        {
            return;
        }

        var who = (await identity.GetUserAsync(domainEvent.ReportedByUserId, cancellationToken))?.FullName
                  ?? (domainEvent.ReportedByRole == IssueReporterRole.Member ? "Un miembro" : "Alguien del equipo");
        var reason = EquipmentNoticeText.Reason(domainEvent.Reason);
        var urgent = domainEvent.Reason == EquipmentIssueReason.Unsafe;

        if (!urgent && await TryGroupAsync(domainEvent, who, reason, cancellationToken))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        var title = urgent
            ? $"URGENTE · {domainEvent.ItemName} es insegura"
            : $"{domainEvent.ItemName} — {reason}";
        var body = $"Reportado por {who}";
        if (!string.IsNullOrWhiteSpace(domainEvent.Description))
        {
            body += $": {domainEvent.Description}";
        }

        var notification = Notification.CreateGymAction(
            domainEvent.GymId, NotificationType.EquipmentIssueReported, domainEvent.IssueId,
            EquipmentNoticeText.Truncate(title, Notification.TitleMaxLength),
            EquipmentNoticeText.Truncate(body, Notification.BodyMaxLength),
            $"/equipment/issues/{domainEvent.IssueId}", clock.UtcNow,
            urgent ? NotificationPriority.Urgent : NotificationPriority.Normal);
        notifications.Add(notification);

        if (urgent)
        {
            await QueueOwnerEmailsAsync(notification, domainEvent, who, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Suma el reporte al aviso sin atender de la misma máquina, si lo hay. Repetir el evento deja el mismo texto.</summary>
    private async Task<bool> TryGroupAsync(
        EquipmentIssueReported domainEvent, string who, string reason, CancellationToken cancellationToken)
    {
        foreach (var otherId in domainEvent.OtherOpenIssueIds)
        {
            var group = await notifications.FindAsync(
                NotificationType.EquipmentIssueReported, null, null, otherId, cancellationToken);
            if (group is null || group.ResolvedAt is not null || group.Priority == NotificationPriority.Urgent)
            {
                continue;
            }

            group.UpdateText(
                EquipmentNoticeText.Truncate(
                    $"{domainEvent.OtherOpenIssueIds.Count + 1} reportes de {domainEvent.ItemName}", Notification.TitleMaxLength),
                EquipmentNoticeText.Truncate($"Último: {reason} · reportado por {who}", Notification.BodyMaxLength));
            return true;
        }

        return false;
    }

    private async Task QueueOwnerEmailsAsync(
        Notification notification, EquipmentIssueReported domainEvent, string who, CancellationToken cancellationToken)
    {
        var owners = await identity.ListActiveOwnersAsync(domainEvent.GymId, cancellationToken);
        if (owners.Count == 0)
        {
            return;
        }

        var gymName = (await gyms.GetBrandingAsync(domainEvent.GymId, cancellationToken))?.Name ?? "Tu gimnasio";
        var title = $"Máquina insegura: {domainEvent.ItemName}";
        var body = $"{who} reportó que «{domainEvent.ItemName}» es insegura.";
        if (!string.IsNullOrWhiteSpace(domainEvent.Description))
        {
            body += $"\n{domainEvent.Description}";
        }

        body += "\nRevísala cuanto antes en el panel: Equipos → Reportes.";

        foreach (var owner in owners.Where(o => !string.IsNullOrWhiteSpace(o.Email)))
        {
            var firstName = owner.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? owner.FullName;
            var (subject, html, text) = NotificationEmails.Compose(gymName, firstName, title, body, null, "Equipos");
            notifications.AddDelivery(NotificationDelivery.CreateEmail(
                domainEvent.GymId, notification.Id, owner.Email, subject, html, text));
        }
    }
}

/// <summary>
/// Al cerrar un reporte (resuelto o descartado): si ya no queda ninguno abierto de esa máquina, su aviso del staff
/// se atiende; si quedan varios, el aviso agrupado actualiza su cuenta. No repite lo ya atendido.
/// </summary>
public sealed class EquipmentIssueClosedUpdatesNotice(
    INotificationRepository notifications,
    IClock clock,
    IUnitOfWork unitOfWork) : IDomainEventHandler<EquipmentIssueClosed>
{
    public async Task HandleAsync(EquipmentIssueClosed domainEvent, CancellationToken cancellationToken = default)
    {
        var changedText = false;
        foreach (var id in domainEvent.ItemIssueIds)
        {
            var notice = await notifications.FindAsync(
                NotificationType.EquipmentIssueReported, null, null, id, cancellationToken);
            if (notice is null || notice.ResolvedAt is not null)
            {
                continue;
            }

            if (domainEvent.OpenIssuesLeft == 0)
            {
                await notifications.TryResolveAsync(notice.Id, domainEvent.ClosedBy, clock.UtcNow, cancellationToken);
            }
            else if (domainEvent.OpenIssuesLeft > 1 && notice.Priority != NotificationPriority.Urgent)
            {
                notice.UpdateText(
                    EquipmentNoticeText.Truncate(
                        $"{domainEvent.OpenIssuesLeft} reportes de {domainEvent.ItemName}", Notification.TitleMaxLength),
                    notice.Body);
                changedText = true;
            }
        }

        if (changedText)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}

/// <summary>
/// Cuando alguien del staff abre un reporte por primera vez deja de estar "sin ver" (como el intento de entrada
/// rechazado). El aviso urgente de ese reporte se atiende de inmediato; el agrupado, cuando ya no queda ningún
/// reporte abierto de la máquina sin ver.
/// </summary>
public sealed class EquipmentIssueSeenResolvesNotice(
    INotificationRepository notifications,
    IClock clock) : IDomainEventHandler<EquipmentIssueSeen>
{
    public async Task HandleAsync(EquipmentIssueSeen domainEvent, CancellationToken cancellationToken = default)
    {
        foreach (var id in domainEvent.ItemIssueIds)
        {
            var notice = await notifications.FindAsync(
                NotificationType.EquipmentIssueReported, null, null, id, cancellationToken);
            if (notice is null || notice.ResolvedAt is not null)
            {
                continue;
            }

            var ownUrgent = notice.ReferenceId == domainEvent.IssueId && notice.Priority == NotificationPriority.Urgent;
            if (ownUrgent || domainEvent.AllOpenSeen)
            {
                await notifications.TryResolveAsync(notice.Id, domainEvent.SeenBy, clock.UtcNow, cancellationToken);
            }
        }
    }
}

/// <summary>
/// Al resolverse el reporte, el miembro que lo hizo recibe "gracias por avisar". Un reporte descartado no genera
/// aviso, ni uno hecho por el staff, ni uno de una cuenta anonimizada. Solo bandeja y push.
/// </summary>
public sealed class EquipmentIssueResolvedNotifiesMember(
    IMemberReader members,
    MemberNoticeWriter writer,
    IUnitOfWork unitOfWork) : IDomainEventHandler<EquipmentIssueClosed>
{
    public async Task HandleAsync(EquipmentIssueClosed domainEvent, CancellationToken cancellationToken = default)
    {
        if (domainEvent.Dismissed
            || domainEvent.ReportedByRole != IssueReporterRole.Member
            || domainEvent.ReportedByUserId is not { } userId)
        {
            return;
        }

        var member = await members.ResolveByUserIdAsync(userId, cancellationToken);
        if (member is null || member.Status != MemberStatus.Active)
        {
            return;
        }

        if (await writer.NotifyAsync(
                new MemberNotice(
                    domainEvent.GymId, member.Id, NotificationType.EquipmentIssueResolved, NotificationCategory.News,
                    domainEvent.IssueId,
                    "Tu reporte se resolvió",
                    $"Lo que reportaste de «{domainEvent.ItemName}» ya quedó resuelto. ¡Gracias por avisar!",
                    "/app/rutina"),
                sendEmail: false, cancellationToken))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
