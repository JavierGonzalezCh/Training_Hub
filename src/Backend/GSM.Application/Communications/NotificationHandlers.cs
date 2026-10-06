using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Communications.Abstractions;
using GSM.Application.Communications.Contracts;
using GSM.Application.Gyms.Public;
using GSM.Application.Members.Public;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Communications;

namespace GSM.Application.Communications;

public sealed class ListMyNotificationsHandler(
    ICurrentUserService currentUser,
    IGymReader gyms,
    IMemberReader members,
    IMemberNotificationRepository notifications)
{
    public async Task<PagedResult<NotificationItem>> HandleAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        await CommunicationsFeatureGate.EnsureMemberModuleEnabledAsync(gyms, gymId, cancellationToken);
        var self = await FeedMember.ResolveAsync(currentUser, members, cancellationToken);

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var (items, total) = await notifications.ListForMemberAsync(self.Id, page, pageSize, cancellationToken);
        return new PagedResult<NotificationItem>(items.Select(ToItem).ToList(), page, pageSize, total);
    }

    internal static NotificationItem ToItem(MemberNotification n)
        => new(n.Id, n.Type.ToString(), n.Title, n.Body, n.Link, n.CreatedAt, n.ReadAt);
}

public sealed class MarkNotificationReadHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberReader members,
    IMemberNotificationRepository notifications,
    IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var self = await FeedMember.ResolveAsync(currentUser, members, cancellationToken);

        // Una notificación de otro miembro del mismo gym se trata como inexistente.
        var notification = await notifications.GetAsync(id, cancellationToken);
        if (notification is null || notification.MemberId != self.Id)
        {
            throw new NotFoundException("Notificación no encontrada.");
        }

        notification.MarkRead(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class MarkAllNotificationsReadHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberReader members,
    IMemberNotificationRepository notifications)
{
    public async Task HandleAsync(CancellationToken cancellationToken)
    {
        var self = await FeedMember.ResolveAsync(currentUser, members, cancellationToken);
        await notifications.MarkAllReadAsync(self.Id, clock.UtcNow, cancellationToken);
    }
}
