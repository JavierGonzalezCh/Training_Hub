using GSM.Application.Notifications.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GSM.Infrastructure.Notifications;

public static class NotificationsInfrastructureRegistration
{
    /// <summary>Registro de la capa Infrastructure del módulo Notifications.</summary>
    public static IServiceCollection AddNotificationsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IMemberNotificationPreferenceRepository, MemberNotificationPreferenceRepository>();
        services.AddScoped<INotificationSettingsRepository, NotificationSettingsRepository>();
        services.AddSingleton<IUnsubscribeLinks, UnsubscribeLinks>();
        services.AddOptions<NotificationLinkOptions>()
            .Bind(configuration.GetSection(NotificationLinkOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<INotificationDispatchLock, PostgresNotificationDispatchLock>();

        services.Configure<NotificationDispatcherOptions>(configuration.GetSection(NotificationDispatcherOptions.SectionName));
        services.AddHostedService<NotificationDispatcher>();

        return services;
    }
}
