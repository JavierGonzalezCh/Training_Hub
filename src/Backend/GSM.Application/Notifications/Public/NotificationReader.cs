using GSM.Application.Notifications.Abstractions;
using GSM.Domain.Notifications;

namespace GSM.Application.Notifications.Public;

internal sealed class NotificationReader(INotificationRepository notifications) : INotificationReader
{
    public Task<IReadOnlyDictionary<Guid, AnnouncementEmailDelivery>> GetAnnouncementEmailDeliveryAsync(
        IReadOnlyCollection<Guid> announcementIds, CancellationToken cancellationToken = default)
        => notifications.GetEmailDeliveryAsync(NotificationType.Announcement, announcementIds, cancellationToken);
}
