using GSM.Application.Common.Abstractions;
using GSM.Application.Communications.Public;
using GSM.Application.Gyms.Public;
using GSM.Application.Members.Public;
using GSM.Application.Notifications.Abstractions;
using GSM.Domain.Members;
using GSM.Domain.Notifications;

namespace GSM.Application.Notifications;

/// <summary>Un aviso personal a un miembro, antes de persistirlo.</summary>
public sealed record MemberNotice(
    Guid GymId,
    Guid MemberId,
    NotificationType Type,
    NotificationCategory Category,
    Guid ReferenceId,
    string Title,
    string Body,
    string? Link);

/// <summary>
/// Crea avisos personales y, si se pide, su correo, de forma idempotente: si ya existe un aviso con la
/// misma <c>(tipo, miembro, referencia)</c>, no hace nada. Lo comparten los manejadores de eventos de
/// Notifications. No guarda: el llamante decide cuándo hacer <c>SaveChanges</c>.
/// <para>
/// El correo respeta el interruptor de baja que hoy es de Comunicaciones (lectura por
/// <see cref="ICommunicationPreferencesReader"/>); las preferencias por categoría llegan en N4.
/// </para>
/// </summary>
public sealed class MemberNoticeWriter(
    INotificationRepository notifications,
    IMemberReader members,
    IGymReader gyms,
    ICommunicationPreferencesReader preferences,
    IClock clock)
{
    /// <returns><c>true</c> si se creó el aviso; <c>false</c> si ya existía.</returns>
    public async Task<bool> NotifyAsync(MemberNotice notice, bool sendEmail, CancellationToken cancellationToken)
    {
        if (await notifications.ExistsAsync(notice.Type, null, notice.MemberId, notice.ReferenceId, cancellationToken))
        {
            return false;
        }

        var notification = Add(notice);
        if (sendEmail)
        {
            var contact = await members.GetContactAsync(notice.MemberId, cancellationToken);
            if (contact is not null)
            {
                var optedOut = await preferences.GetEmailOptedOutAsync([contact.Id], cancellationToken);
                var gymName = await GymNameAsync(notice.GymId, cancellationToken);
                AddEmail(notification, contact, gymName, optedOut, notice.Title, notice.Body);
            }
        }

        return true;
    }

    /// <summary>
    /// Un aviso por miembro de la lista (que ya recibió el aviso, se salta) y, si <paramref name="emailBody"/>
    /// no es nulo, su correo con ese cuerpo completo.
    /// </summary>
    /// <returns>Cuántos avisos se crearon y cuántos correos se encolaron.</returns>
    public async Task<(int Notices, int Emails)> NotifyManyAsync(
        Guid gymId,
        IReadOnlyCollection<Guid> memberIds,
        NotificationType type,
        NotificationCategory category,
        Guid referenceId,
        string title,
        string body,
        string? link,
        string? emailBody,
        CancellationToken cancellationToken)
    {
        var already = await notifications.GetNotifiedMemberIdsAsync(type, referenceId, cancellationToken);
        var targets = memberIds.Distinct().Where(id => !already.Contains(id)).ToList();
        if (targets.Count == 0)
        {
            return (0, 0);
        }

        IReadOnlyDictionary<Guid, MemberContactView> contacts = new Dictionary<Guid, MemberContactView>();
        IReadOnlySet<Guid> optedOut = new HashSet<Guid>();
        var gymName = string.Empty;
        if (emailBody is not null)
        {
            var wanted = targets.ToHashSet();
            contacts = (await members.GetEmailContactsAsync(gymId, cancellationToken))
                .Where(c => wanted.Contains(c.Id))
                .ToDictionary(c => c.Id);
            optedOut = await preferences.GetEmailOptedOutAsync(contacts.Keys.ToList(), cancellationToken);
            gymName = await GymNameAsync(gymId, cancellationToken);
        }

        var emails = 0;
        foreach (var memberId in targets)
        {
            var notification = Add(new MemberNotice(gymId, memberId, type, category, referenceId, title, body, link));
            if (emailBody is not null
                && contacts.TryGetValue(memberId, out var contact)
                && AddEmail(notification, contact, gymName, optedOut, title, emailBody))
            {
                emails++;
            }
        }

        return (targets.Count, emails);
    }

    private Notification Add(MemberNotice notice)
    {
        var notification = Notification.CreateForMember(
            notice.GymId, notice.MemberId, notice.Type, notice.Category, notice.ReferenceId,
            notice.Title, Truncate(notice.Body, Notification.BodyMaxLength), notice.Link, clock.UtcNow);
        notifications.Add(notification);
        return notification;
    }

    private bool AddEmail(
        Notification notification, MemberContactView contact, string gymName,
        IReadOnlySet<Guid> optedOut, string title, string body)
    {
        if (contact.Status != MemberStatus.Active
            || string.IsNullOrWhiteSpace(contact.Email)
            || optedOut.Contains(contact.Id))
        {
            return false;
        }

        var (subject, html, text) = NotificationEmails.Compose(
            gymName, contact.FirstName, title, body, preferences.BuildUnsubscribeUrl(contact.Id, notification.GymId));
        notifications.AddDelivery(NotificationDelivery.CreateEmail(
            notification.GymId, notification.Id, contact.Email, subject, html, text));
        return true;
    }

    private async Task<string> GymNameAsync(Guid gymId, CancellationToken cancellationToken)
        => (await gyms.GetBrandingAsync(gymId, cancellationToken))?.Name ?? "Tu gimnasio";

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..(max - 1)] + "…";
}
