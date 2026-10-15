using GSM.Application.Notifications.Abstractions;
using Microsoft.Extensions.Configuration;
using GSM.Application.Notifications;
using GSM.Infrastructure.Notifications.Push;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
        services.AddScoped<IPushDeviceRepository, PushDeviceRepository>();
        services.AddScoped<IStaffNotificationPreferenceRepository, StaffNotificationPreferenceRepository>();
        services.AddSingleton<INotificationDispatchLock, PostgresNotificationDispatchLock>();

        services.AddSingleton<IPushWebConfigProvider, PushWebConfigProvider>();

        // Push (N2): con Firebase configurado envía de verdad; sin él, la API arranca y el push se omite.
        services.Configure<FcmOptions>(configuration.GetSection(FcmOptions.SectionName));
        var fcm = configuration.GetSection(FcmOptions.SectionName).Get<FcmOptions>() ?? new FcmOptions();
        if (fcm.IsConfigured)
        {
            services.AddHttpClient("fcm");
            services.AddSingleton<IPushSender>(sp => new FcmPushSender(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("fcm"),
                sp.GetRequiredService<IOptions<FcmOptions>>(),
                sp.GetRequiredService<ILogger<FcmPushSender>>(),
                sp.GetRequiredService<GSM.Application.Common.Logs.ILogStore>()));
        }
        else
        {
            services.AddSingleton<IPushSender, LoggingPushSender>();
        }

        services.Configure<NotificationDispatcherOptions>(configuration.GetSection(NotificationDispatcherOptions.SectionName));
        services.AddHostedService<NotificationDispatcher>();

        return services;
    }
}
