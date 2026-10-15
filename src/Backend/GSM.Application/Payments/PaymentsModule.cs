using Microsoft.Extensions.DependencyInjection;
using GSM.Application.Payments;

namespace GSM.Application;

public static class PaymentsModuleRegistration
{
    /// <summary>Registro manual de la capa Application del módulo Payments.</summary>
    public static IServiceCollection AddPaymentsModule(this IServiceCollection services)
    {
        // Los casos de uso (*Handler), validators y handlers de eventos se registran por assembly scanning.
        // Añade aquí solo lo que el scanning no cubra.

        services.AddScoped<CashDrawerService>();

        return services;
    }
}
