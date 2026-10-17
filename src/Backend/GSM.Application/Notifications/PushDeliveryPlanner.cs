using GSM.Application.Identity.Public;
using GSM.Application.Members.Public;
using GSM.Application.Notifications.Abstractions;
using GSM.Domain.Members;
using GSM.Domain.Notifications;

namespace GSM.Application.Notifications;

/// <summary>
/// Decide a qué dispositivos va el push de un aviso y crea una entrega por dispositivo (spec §6, §8, §10).
/// <list type="bullet">
/// <item>Miembro: lo que diga su preferencia de push para la categoría (sin fila, el valor por defecto). El
/// <b>anuncio importante</b> y los avisos de seguridad no tienen preferencia. Si la apagó, queda una entrega
/// <c>skipped</c> como rastro y no se envía nada.</item>
/// <item>Persona del equipo: lo que ella eligió para ese tipo de aviso (§8).</item>
/// <item>Aviso de acción del gym (sin destinatario): quienes pidieron su push.</item>
/// </list>
/// El horario sin avisos no se mira aquí: lo resuelve el dispatcher antes de llamar. No guarda.
/// </summary>
public sealed class PushDeliveryPlanner(
    IMemberReader members,
    IMemberNotificationPreferenceRepository memberPreferences,
    IStaffNotificationPreferenceRepository staffPreferences,
    IPushDeviceRepository devices,
    INotificationRepository notifications,
    IIdentityReader identity)
{
    private static readonly PushPlatform[] MemberPlatforms = [PushPlatform.Android, PushPlatform.WebMember];
    private static readonly PushPlatform[] StaffPlatforms = [PushPlatform.Android, PushPlatform.WebPanel];

    public async Task<IReadOnlyList<NotificationDelivery>> PlanAsync(
        Notification notification, CancellationToken cancellationToken)
    {
        var created = new List<NotificationDelivery>();

        if (notification.RecipientMemberId is { } memberId)
        {
            await PlanForMemberAsync(notification, memberId, created, cancellationToken);
        }
        else if (notification.RecipientUserId is { } userId)
        {
            var preferences = await staffPreferences.GetForUsersAsync(notification.GymId, [userId], cancellationToken);
            var enabled = preferences.TryGetValue((userId, notification.Type), out var push)
                ? push
                : StaffNotificationPreference.DefaultPush(notification.Type);
            if (!enabled)
            {
                created.Add(NotificationDelivery.CreateSkippedPush(
                    notification.GymId, notification.Id, "Push apagado en las preferencias del staff."));
            }
            else
            {
                await AddDevicesAsync(notification, userId, StaffPlatforms, created, cancellationToken);
            }
        }
        else if (notification.RequiresAction)
        {
            var userIds = (await staffPreferences.GetUserIdsWithPushAsync(
                notification.GymId, notification.Type, cancellationToken)).ToList();

            // A7-1: el push del intento rechazado viene encendido para el Owner mientras no lo apague: se suman los
            // Owners activos sin preferencia guardada (con fila manda su elección, ya sea en la lista o no).
            if (StaffNotificationPreference.DefaultPush(notification.Type, isOwner: true))
            {
                var owners = (await identity.ListActiveOwnersAsync(notification.GymId, cancellationToken))
                    .Select(o => o.UserId).ToList();
                var saved = await staffPreferences.GetForUsersAsync(notification.GymId, owners, cancellationToken);
                userIds.AddRange(owners.Where(id => !saved.ContainsKey((id, notification.Type)) && !userIds.Contains(id)));
            }

            // N1b: un aviso con permisos requeridos solo va a quien los tenga (el Owner siempre).
            IReadOnlySet<Guid>? allowed = null;
            if (notification.RequiredPermissions is { Length: > 0 } required)
            {
                allowed = await identity.ListUserIdsWithAnyPermissionAsync(
                    notification.GymId, StaffNoticeAudience.Parse(required), cancellationToken);
            }

            foreach (var staffUserId in userIds.Where(id => allowed is null || allowed.Contains(id)))
            {
                await AddDevicesAsync(notification, staffUserId, StaffPlatforms, created, cancellationToken);
            }
        }

        foreach (var delivery in created)
        {
            notifications.AddDelivery(delivery);
        }

        return created;
    }

    private async Task PlanForMemberAsync(
        Notification notification, Guid memberId, List<NotificationDelivery> created, CancellationToken cancellationToken)
    {
        var member = await members.GetAsync(memberId, cancellationToken);
        if (member is null || member.Status != MemberStatus.Active || member.UserId is not { } userId)
        {
            return; // sin cuenta no hay dispositivo: queda la bandeja y, si el tipo lo prevé, el correo
        }

        if (!BypassesPreferences(notification)
            && notification.Category is { } category
            && MemberNotificationPreference.IsConfigurable(category))
        {
            var rows = await memberPreferences.GetForMembersAsync([memberId], category, cancellationToken);
            var enabled = rows.TryGetValue(memberId, out var row)
                ? row.Push
                : MemberNotificationPreference.Defaults(category).Push;
            if (!enabled)
            {
                created.Add(NotificationDelivery.CreateSkippedPush(
                    notification.GymId, notification.Id, "Push apagado por el miembro para esta categoría."));
                return;
            }
        }

        await AddDevicesAsync(notification, userId, MemberPlatforms, created, cancellationToken);
    }

    private async Task AddDevicesAsync(
        Notification notification, Guid userId, IReadOnlyCollection<PushPlatform> platforms,
        List<NotificationDelivery> created, CancellationToken cancellationToken)
    {
        foreach (var device in await devices.GetActiveForUserAsync(userId, platforms, cancellationToken))
        {
            created.Add(NotificationDelivery.CreatePush(notification.GymId, notification.Id, device.Id));
        }
    }

    /// <summary>El anuncio importante y los avisos de seguridad no tienen preferencia (§10).</summary>
    internal static bool BypassesPreferences(Notification notification)
        => notification.Type == NotificationType.ImportantAnnouncement
            || notification.Category == NotificationCategory.Security;
}
