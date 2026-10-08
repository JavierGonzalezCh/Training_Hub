using Microsoft.Extensions.DependencyInjection;

namespace GSM.Application;

public static class CommunicationsModuleRegistration
{
    /// <summary>Registro manual de la capa Application del módulo Communications.</summary>
    public static IServiceCollection AddCommunicationsModule(this IServiceCollection services)
    {
        // Servicio compartido (no es un caso de uso): lo usan handlers de eventos y la tarea periódica.
        services.AddScoped<GSM.Application.Communications.MemberNotifier>();

        return services;
    }
}
