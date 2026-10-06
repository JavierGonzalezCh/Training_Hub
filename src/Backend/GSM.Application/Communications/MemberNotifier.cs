using GSM.Application.Communications.Abstractions;
using GSM.Application.Gyms.Public;
using GSM.Application.Members.Public;
using GSM.Domain.Communications;
using GSM.Domain.Members;

namespace GSM.Application.Communications;

/// <summary>
/// Crea avisos personales y encola sus correos, de forma idempotente: si ya existe un aviso con la
/// misma <c>(miembro, tipo, referencia)</c>, no hace nada. Lo comparten los handlers de eventos, la
/// publicación de anuncios y la tarea periódica. No guarda: el llamante decide cuándo hacer
/// <c>SaveChanges</c>.
/// </summary>
public sealed class MemberNotifier(
    IMemberNotificationRepository notifications,
    IEmailOutboxRepository outbox,
    IMemberCommunicationPreferencesRepository preferences,
    IMemberReader members,
    IGymReader gyms,
    IUnsubscribeLinks unsubscribeLinks)
{
    /// <returns><c>true</c> si se creó el aviso; <c>false</c> si ya existía.</returns>
    public async Task<bool> NotifyAsync(
        Guid gymId,
        Guid memberId,
        NotificationType type,
        Guid referenceId,
        string title,
        string body,
        string? link,
        bool sendEmail,
        CancellationToken cancellationToken)
    {
        if (await notifications.ExistsAsync(memberId, type, referenceId, cancellationToken))
        {
            return false;
        }

        notifications.Add(MemberNotification.Create(gymId, memberId, type, referenceId, title, body, link));

        if (sendEmail && EmailKindFor(type) is { } kind)
        {
            var contact = await members.GetContactAsync(memberId, cancellationToken);
            if (contact is not null)
            {
                await EnqueueAsync(gymId, [contact], kind, referenceId, title, body, cancellationToken);
            }
        }

        return true;
    }

    /// <summary>
    /// Encola un correo por destinatario, saltándose a los inactivos, a los que no tienen email, a
    /// los que se dieron de baja y a los que ya tienen ese correo en la cola.
    /// </summary>
    /// <returns>Cuántos correos se encolaron.</returns>
    public async Task<int> EnqueueAsync(
        Guid gymId,
        IReadOnlyCollection<MemberContactView> recipients,
        EmailKind kind,
        Guid referenceId,
        string title,
        string body,
        CancellationToken cancellationToken)
    {
        var candidates = recipients
            .Where(r => r.Status == MemberStatus.Active && !string.IsNullOrWhiteSpace(r.Email))
            .ToList();
        if (candidates.Count == 0)
        {
            return 0;
        }

        var optedOut = await preferences.GetOptedOutAsync(candidates.Select(c => c.Id).ToList(), cancellationToken);
        var gymName = (await gyms.GetBrandingAsync(gymId, cancellationToken))?.Name ?? "Tu gimnasio";

        var queued = 0;
        foreach (var recipient in candidates.Where(c => !optedOut.Contains(c.Id)))
        {
            if (await outbox.ExistsAsync(recipient.Id, kind, referenceId, cancellationToken))
            {
                continue;
            }

            var (subject, html, text) = CommunicationEmails.Compose(
                gymName, recipient.FirstName, title, body, unsubscribeLinks.BuildUrl(recipient.Id, gymId));
            outbox.Add(EmailOutboxItem.Create(
                gymId, recipient.Id, recipient.Email!, subject, html, text, kind, referenceId));
            queued++;
        }

        return queued;
    }

    /// <summary>Los logros no van por email: un correo por cada logro sería ruido.</summary>
    private static EmailKind? EmailKindFor(NotificationType type) => type switch
    {
        NotificationType.ClassCancelled => EmailKind.ClassCancelled,
        NotificationType.MembershipExpiring => EmailKind.MembershipExpiring,
        _ => null,
    };
}
