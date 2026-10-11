using GSM.Application.Communications.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GSM.Infrastructure.Communications;

public static class CommunicationsInfrastructureRegistration
{
    /// <summary>Registro de la capa Infrastructure del módulo Communications.</summary>
    public static IServiceCollection AddCommunicationsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IAnnouncementRepository, AnnouncementRepository>();
        services.AddScoped<IAnnouncementReactionRepository, AnnouncementReactionRepository>();
        services.AddScoped<IMemberFeedStateRepository, MemberFeedStateRepository>();
        services.AddScoped<ICommunicationsSettingsRepository, CommunicationsSettingsRepository>();
        services.AddScoped<IWhatsAppRepository, WhatsAppRepository>();
        services.AddOptions<CommunicationsOptions>()
            .Bind(configuration.GetSection(CommunicationsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}
