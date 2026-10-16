using Microsoft.Extensions.DependencyInjection;

namespace GSM.Application;

public static class LogsModuleRegistration
{
    /// <summary>Registro manual de la capa Application del panel de errores (LG4).</summary>
    public static IServiceCollection AddLogsModule(this IServiceCollection services)
    {
        // Los casos de uso (*Handler) se registran por assembly scanning.
        services.AddScoped<Logs.EventFailureReplayer>();
        services.AddScoped<Logs.Alerts.PlatformAlertEvaluator>(); // LG5

        return services;
    }
}
