using Microsoft.Extensions.DependencyInjection;

namespace GSM.Application;

public static class AchievementsModuleRegistration
{
    /// <summary>Registro manual de la capa Application del módulo Achievements.</summary>
    public static IServiceCollection AddAchievementsModule(this IServiceCollection services)
    {
        // Los casos de uso (*Handler), validators y handlers de eventos se registran por assembly scanning.
        // Añade aquí solo lo que el scanning no cubra.

        return services;
    }
}
