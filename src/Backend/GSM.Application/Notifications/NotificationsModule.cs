using GSM.Application.Notifications;
using GSM.Application.Notifications.Public;
using Microsoft.Extensions.DependencyInjection;

namespace GSM.Application;

public static class NotificationsModuleRegistration
{
    /// <summary>
    /// Registro manual de la capa Application del módulo Notifications. Los manejadores de eventos y los
    /// casos de uso (<c>*Handler</c>) los registra el escaneo de ensamblado.
    /// </summary>
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services)
    {
        services.AddScoped<MemberNoticeWriter>();
        services.AddScoped<ScheduledNoticeService>();
        services.AddScoped<PushDeliveryPlanner>();
        services.AddScoped<NotificationDispatchRunner>();
        services.AddScoped<INotificationReader, NotificationReader>();
        services.AddScoped<INotificationPreferenceReader, NotificationPreferenceReader>();
        services.AddScoped<IPlatformAlertNotifier, PlatformAlertNotifier>(); // LG5: alertas al SuperAdmin

        return services;
    }
}
