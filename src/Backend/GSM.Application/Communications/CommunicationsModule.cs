using GSM.Application.Communications;
using GSM.Application.Communications.Public;
using Microsoft.Extensions.DependencyInjection;

namespace GSM.Application;

public static class CommunicationsModuleRegistration
{
    /// <summary>Registro manual de la capa Application del módulo Communications.</summary>
    public static IServiceCollection AddCommunicationsModule(this IServiceCollection services)
    {
        services.AddScoped<ICommunicationPreferencesReader, CommunicationPreferencesReader>();

        return services;
    }
}
