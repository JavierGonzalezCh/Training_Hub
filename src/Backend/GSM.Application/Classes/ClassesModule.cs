using GSM.Application.Classes;
using Microsoft.Extensions.DependencyInjection;

namespace GSM.Application;

public static class ClassesModuleRegistration
{
    /// <summary>Registro manual de la capa Application del módulo Classes.</summary>
    public static IServiceCollection AddClassesModule(this IServiceCollection services)
    {
        // Los casos de uso (*Handler), validators y handlers de eventos se registran por assembly scanning.
        // Añade aquí solo lo que el scanning no cubra.

        services.AddScoped<ClosedSessionCanceller>();

        return services;
    }
}
