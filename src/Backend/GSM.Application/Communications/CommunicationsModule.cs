using GSM.Application.Communications;
using Microsoft.Extensions.DependencyInjection;

namespace GSM.Application;

public static class CommunicationsModuleRegistration
{
    /// <summary>Registro manual de la capa Application del módulo Communications.</summary>
    public static IServiceCollection AddCommunicationsModule(this IServiceCollection services)
    {
        return services;
    }
}
