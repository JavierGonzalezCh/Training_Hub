using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Identity.Public;
using GSM.Application.Notifications.Abstractions;
using GSM.Application.Notifications.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Notifications;

/// <summary>
/// Bandeja del staff (la campana del panel). Un aviso de acción es uno por gym y se atiende para todos;
/// uno informativo es de una persona y ella lo marca como leído.
/// </summary>
public sealed class ListStaffNotificationsHandler(
    ICurrentUserService currentUser,
    IClock clock,
    INotificationRepository notifications,
    IIdentityReader identity)
{
    public async Task<PagedResult<StaffNotificationItem>> HandleAsync(
        bool openOnly, int page, int pageSize, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        var userId = currentUser.UserId ?? throw new UnauthorizedDomainException("Sin usuario en el contexto.");

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var (items, total) = await notifications.ListForStaffAsync(
            userId, StaffNoticeAudience.Held(currentUser), clock.UtcNow, openOnly, page, pageSize, cancellationToken);

        var names = await StaffNotificationMapper.ResolveNamesAsync(identity, items, cancellationToken);
        return new PagedResult<StaffNotificationItem>(
            items.Select(n => StaffNotificationMapper.ToItem(n, names)).ToList(), page, pageSize, total);
    }
}

/// <summary>Cuántos avisos tiene abiertos el staff: de acción sin atender e informativos propios sin leer.</summary>
public sealed class GetStaffNotificationCountHandler(
    ICurrentUserService currentUser, IClock clock, INotificationRepository notifications)
{
    public async Task<StaffNotificationCount> HandleAsync(CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        var userId = currentUser.UserId ?? throw new UnauthorizedDomainException("Sin usuario en el contexto.");
        return new StaffNotificationCount(await notifications.CountOpenForStaffAsync(
            userId, StaffNoticeAudience.Held(currentUser), clock.UtcNow, cancellationToken));
    }
}

/// <summary>Atiende un aviso de acción para todo el staff. Atenderlo dos veces no cambia quién lo atendió.</summary>
public sealed class ResolveStaffNotificationHandler(
    ICurrentUserService currentUser,
    IClock clock,
    INotificationRepository notifications,
    IIdentityReader identity,
    IDomainEventPublisher events,
    ILogger<ResolveStaffNotificationHandler> logger)
{
    public async Task<StaffNotificationItem> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        var userId = currentUser.UserId ?? throw new UnauthorizedDomainException("Sin usuario en el contexto.");

        var notification = await notifications.GetUntrackedAsync(id, cancellationToken)
            ?? throw new NotFoundException("Aviso no encontrado.");
        if (!notification.RequiresAction)
        {
            throw new ConflictException("Este aviso no es de acción: márcalo como leído.");
        }

        // N1b: un aviso que no le corresponde se trata como inexistente; el de anulación lo cierra quien la resuelve.
        var held = StaffNoticeAudience.Held(currentUser);
        if (!StaffNoticeAudience.CanSee(notification, held))
        {
            throw new NotFoundException("Aviso no encontrado.");
        }

        if (!StaffNoticeAudience.CanResolve(notification, held))
        {
            throw new ForbiddenDomainException("Este aviso se cierra al aprobar o rechazar la solicitud.");
        }

        var now = clock.UtcNow;
        if (await notifications.TryResolveAsync(id, userId, now, cancellationToken))
        {
            logger.LogInformation("Aviso {NotificationId} atendido por {UserId}", id, userId);
            await events.PublishAsync(
                new ActionNoticeResolved(
                    notification.GymId, StaffNotificationMapper.KindOf(notification.Type),
                    notification.ReferenceId, userId, now),
                cancellationToken);
        }

        // Se relee: si otra persona lo atendió antes, la respuesta dice quién.
        var fresh = await notifications.GetUntrackedAsync(id, cancellationToken) ?? notification;
        var names = await StaffNotificationMapper.ResolveNamesAsync(identity, [fresh], cancellationToken);
        return StaffNotificationMapper.ToItem(fresh, names);
    }
}

/// <summary>Atiende de una vez todos los avisos de acción abiertos del gym.</summary>
public sealed class ResolveAllStaffNotificationsHandler(
    ICurrentUserService currentUser,
    IClock clock,
    INotificationRepository notifications,
    IDomainEventPublisher events,
    ILogger<ResolveAllStaffNotificationsHandler> logger)
{
    public async Task<int> HandleAsync(CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        var userId = currentUser.UserId ?? throw new UnauthorizedDomainException("Sin usuario en el contexto.");

        var now = clock.UtcNow;
        var resolved = 0;
        var held = StaffNoticeAudience.Held(currentUser);
        foreach (var id in await notifications.GetOpenActionIdsAsync(held, now, cancellationToken))
        {
            var notification = await notifications.GetUntrackedAsync(id, cancellationToken);
            if (notification is null || !StaffNoticeAudience.CanResolve(notification, held)
                || !await notifications.TryResolveAsync(id, userId, now, cancellationToken))
            {
                continue;
            }

            resolved++;
            await events.PublishAsync(
                new ActionNoticeResolved(
                    notification.GymId, StaffNotificationMapper.KindOf(notification.Type),
                    notification.ReferenceId, userId, now),
                cancellationToken);
        }

        logger.LogInformation("{Count} avisos de acción atendidos por {UserId}", resolved, userId);
        return resolved;
    }
}

/// <summary>Marca como leído un aviso informativo propio.</summary>
public sealed class MarkStaffNotificationReadHandler(
    ICurrentUserService currentUser,
    IClock clock,
    INotificationRepository notifications,
    IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedDomainException("Sin usuario en el contexto.");

        // Un aviso de otra persona se trata como inexistente; uno de acción se atiende, no se lee.
        var notification = await notifications.GetAsync(id, cancellationToken);
        if (notification is null || notification.RequiresAction || notification.RecipientUserId != userId)
        {
            throw new NotFoundException("Aviso no encontrado.");
        }

        notification.MarkRead(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Tarea periódica: purga avisos de más de 90 días (todos los gyms; ver <see cref="INotificationRepository.PurgeAsync"/>).</summary>
public sealed class PurgeNotificationsHandler(IClock clock, INotificationRepository notifications)
{
    public Task<int> HandleAsync(CancellationToken cancellationToken)
        => notifications.PurgeAsync(clock.UtcNow.AddDays(-Notification.RetentionDays), cancellationToken);
}

internal static class StaffNotificationMapper
{
    public static string KindOf(NotificationType type) => type switch
    {
        NotificationType.CheckinRejected => ActionNoticeKinds.CheckinRejected,
        _ => type.ToString(),
    };

    public static async Task<IReadOnlyDictionary<Guid, string>> ResolveNamesAsync(
        IIdentityReader identity, IEnumerable<Notification> items, CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, string>();
        foreach (var id in items.Where(n => n.ResolvedBy is not null).Select(n => n.ResolvedBy!.Value).Distinct())
        {
            if (await identity.GetUserAsync(id, cancellationToken) is { } user)
            {
                names[id] = user.FullName;
            }
        }

        return names;
    }

    public static StaffNotificationItem ToItem(Notification n, IReadOnlyDictionary<Guid, string> names) => new(
        n.Id, n.Type.ToString(), n.Title, n.Body, n.Link, n.Priority.ToString(), n.RequiresAction,
        n.CreatedAt, n.ReadAt, n.ResolvedAt, n.ResolvedBy,
        n.ResolvedBy is { } by && names.TryGetValue(by, out var name) ? name : null);
}
