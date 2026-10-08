using Microsoft.Extensions.DependencyInjection;

namespace GSM.Application;

public static class MembershipsModuleRegistration
{
    /// <summary>Registro manual de la capa Application del módulo Memberships.</summary>
    public static IServiceCollection AddMembershipsModule(this IServiceCollection services)
    {
        // Los casos de uso (*Handler), validators y handlers de eventos se registran por assembly scanning.
        // Añade aquí solo lo que el scanning no cubra.

        return services;
    }
}
