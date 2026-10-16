using GSM.Application.Logs.Alerts;
using GSM.Application.Notifications;
using GSM.Application.Notifications.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace GSM.Infrastructure.Notifications;

/// <summary>Configuración de <c>Notifications:Dispatcher</c>.</summary>
public sealed class NotificationDispatcherOptions
{
    public const string SectionName = "Notifications:Dispatcher";

    /// <summary>Apagado en los tests: los tests ejecutan una vuelta a mano para ser deterministas.</summary>
    public bool Enabled { get; set; } = true;

    public int IntervalSeconds { get; set; } = 30;
}

/// <summary>
/// El primer proceso en segundo plano del sistema (spec §5): cada 30 segundos ejecuta una vuelta de
/// <see cref="NotificationDispatchRunner"/>. El candado de Postgres evita que dos copias de la API
/// envíen lo mismo; si la que lo tiene se cae, la otra toma el relevo en la vuelta siguiente.
/// <para>
/// LG5: cada <c>Logs:Alerts:EvaluationIntervalSeconds</c> evalúa además las alertas al SuperAdmin. Es seguro en
/// varias copias (la repetición y la entrega se reclaman en la base) y no depende del candado del dispatcher.
/// </para>
/// </summary>
public sealed class NotificationDispatcher(
    IServiceScopeFactory scopeFactory,
    Microsoft.Extensions.Options.IOptions<NotificationDispatcherOptions> options,
    PlatformAlertOptions alertOptions,
    ILogger<NotificationDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("El dispatcher de avisos está desactivado por configuración");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.IntervalSeconds));
        var alertEvery = TimeSpan.FromSeconds(Math.Max(1, alertOptions.EvaluationIntervalSeconds));
        var nextAlertEvaluation = DateTimeOffset.MinValue;
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<NotificationDispatchRunner>().RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // El runner ya dejó el error en logs.errors; el proceso sigue vivo para la vuelta siguiente.
                logger.LogError(ex, "Falló una vuelta del dispatcher de avisos");
            }

            if (alertOptions.Enabled && DateTimeOffset.UtcNow >= nextAlertEvaluation)
            {
                nextAlertEvaluation = DateTimeOffset.UtcNow + alertEvery;
                await EvaluateAlertsAsync(stoppingToken);
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private async Task EvaluateAlertsAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<PlatformAlertEvaluator>()
                .RunOnceAsync([NotificationDispatchRunner.Name], stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // El evaluador no lanza; esto es la última red para que el dispatcher siga vivo pase lo que pase.
            logger.LogError(ex, "Falló la evaluación de alertas al SuperAdmin");
        }
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}

/// <summary>
/// <c>pg_try_advisory_lock</c> sobre una conexión propia (el candado es de sesión). Se libera al desechar,
/// y si el proceso muere, Postgres lo libera al cerrarse la conexión.
/// </summary>
public sealed class PostgresNotificationDispatchLock(IConfiguration configuration) : INotificationDispatchLock
{
    /// <summary>Clave del candado: "GSM" + N1.</summary>
    public const long LockKey = 0x47534D4E31;

    public async Task<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken = default)
    {
        var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres"));
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
            command.Parameters.AddWithValue("key", LockKey);
            if ((bool)(await command.ExecuteScalarAsync(cancellationToken))!)
            {
                return new Held(connection);
            }
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }

        await connection.DisposeAsync();
        return null;
    }

    private sealed class Held(NpgsqlConnection connection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
                command.Parameters.AddWithValue("key", LockKey);
                await command.ExecuteScalarAsync();
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
