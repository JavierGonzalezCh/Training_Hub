using GSM.Application.Common.Logs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GSM.Infrastructure.Logs;

public static class LogsInfrastructureRegistration
{
    /// <summary>
    /// El schema <c>logs</c>: su propio DbContext y conexión (<c>ConnectionStrings:Logs</c>, que cae a la
    /// principal si no está), el escritor en segundo plano y el mantenimiento.
    /// </summary>
    public static IServiceCollection AddLogsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Logs")
            ?? configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Falta la connection string 'ConnectionStrings:Postgres'.");

        services.AddDbContextFactory<LogsDbContext>(options =>
            options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());

        services.AddSingleton<LogStore>();
        services.AddSingleton<ILogStore>(sp => sp.GetRequiredService<LogStore>());
        services.AddHostedService(sp => sp.GetRequiredService<LogStore>());

        services.AddSingleton<LogMaintenance>();
        services.AddSingleton<ILogMaintenance>(sp => sp.GetRequiredService<LogMaintenance>());

        return services;
    }
}
