using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms.Public;
using GSM.Application.Members.Public;
using GSM.Application.Notifications.Abstractions;
using GSM.Application.Notifications.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Notifications;

namespace GSM.Application.Notifications;

/// <summary>Bandeja del miembro (la campana de <c>/app</c>). Los anuncios no aparecen: viven en el feed.</summary>
public sealed class ListMyNotificationsHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    IMemberReader members,
    INotificationRepository notifications)
{
    public async Task<PagedResult<NotificationItem>> HandleAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        await MemberInbox.EnsureModuleEnabledAsync(gyms, gymId, cancellationToken);
        var self = await MemberInbox.ResolveAsync(currentUser, members, cancellationToken);

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var (items, total) = await notifications.ListForMemberAsync(self.Id, clock.UtcNow, page, pageSize, cancellationToken);
        return new PagedResult<NotificationItem>(items.Select(ToItem).ToList(), page, pageSize, total);
    }

    internal static NotificationItem ToItem(Notification n)
        => new(n.Id, n.Type.ToString(), n.Title, n.Body, n.Link, n.CreatedAt, n.ReadAt);
}

public sealed class MarkNotificationReadHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberReader members,
    INotificationRepository notifications,
    IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var self = await MemberInbox.ResolveAsync(currentUser, members, cancellationToken);

        // Un aviso de otro miembro del mismo gym se trata como inexistente.
        var notification = await notifications.GetAsync(id, cancellationToken);
        if (notification is null || notification.RecipientMemberId != self.Id)
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
    INotificationRepository notifications)
{
    public async Task HandleAsync(CancellationToken cancellationToken)
    {
        var self = await MemberInbox.ResolveAsync(currentUser, members, cancellationToken);
        await notifications.MarkAllReadForMemberAsync(self.Id, clock.UtcNow, cancellationToken);
    }
}

/// <summary>Cuántos avisos sin leer tiene el miembro (para el indicador de la app).</summary>
public sealed class CountMyUnreadNotificationsHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberReader members,
    INotificationRepository notifications)
{
    public async Task<int> HandleAsync(CancellationToken cancellationToken)
    {
        var self = await MemberInbox.ResolveAsync(currentUser, members, cancellationToken);
        return await notifications.CountUnreadForMemberAsync(self.Id, clock.UtcNow, cancellationToken);
    }
}

internal static class MemberInbox
{
    /// <summary>
    /// Con <c>show_community = false</c> (reinterpretado como "mostrar comunicaciones") la bandeja del
    /// miembro responde 403, como hasta ahora.
    /// </summary>
    public static async Task EnsureModuleEnabledAsync(IGymReader gyms, Guid gymId, CancellationToken cancellationToken)
    {
        var flags = await gyms.GetFeatureFlagsAsync(gymId, cancellationToken);
        if (flags is null || !flags.ShowCommunity)
        {
            throw new ForbiddenDomainException("Las comunicaciones no están habilitadas en este gimnasio.");
        }
    }

    public static async Task<MemberInfo> ResolveAsync(
        ICurrentUserService currentUser, IMemberReader members, CancellationToken cancellationToken)
        => await members.ResolveByUserIdAsync(currentUser.UserId ?? Guid.Empty, cancellationToken)
            ?? throw new ForbiddenDomainException("Tu cuenta no está vinculada a un miembro.");
}
