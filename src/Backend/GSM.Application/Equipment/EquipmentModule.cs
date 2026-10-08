using Microsoft.Extensions.DependencyInjection;

namespace GSM.Application;

public static class EquipmentModuleRegistration
{
    /// <summary>Registro manual de la capa Application del módulo Equipment.</summary>
    public static IServiceCollection AddEquipmentModule(this IServiceCollection services)
    {
        // Los casos de uso (*Handler), validators y handlers de eventos se registran por assembly scanning.
        // Añade aquí solo lo que el scanning no cubra.

        return services;
    }
}
