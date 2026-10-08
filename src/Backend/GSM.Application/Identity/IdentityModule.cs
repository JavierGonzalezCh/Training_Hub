using GSM.Application.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace GSM.Application;

public static class IdentityModuleRegistration
{
    /// <summary>Registro manual de la capa Application del módulo Identity.</summary>
    public static IServiceCollection AddIdentityModule(this IServiceCollection services)
    {
        // Los casos de uso (*Handler), validators y handlers de eventos se registran por assembly scanning.
        // Añade aquí solo lo que el scanning no cubra.
        services.AddScoped<SessionIssuer>();

        return services;
    }
}
