using Microsoft.Extensions.DependencyInjection;
using GSM.Application.Routines;

namespace GSM.Application;

public static class RoutinesModuleRegistration
{
    /// <summary>Registro manual de la capa Application del módulo Routines.</summary>
    public static IServiceCollection AddRoutinesModule(this IServiceCollection services)
    {
        // Los casos de uso (*Handler), validators y handlers de eventos se registran por assembly scanning.
        // Añade aquí solo lo que el scanning no cubra.

        services.AddScoped<ExerciseEquipmentResolver>();
        services.AddScoped<ExerciseAlternativesResolver>();

        return services;
    }
}
