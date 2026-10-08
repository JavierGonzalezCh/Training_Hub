using GSM.Application.Common.Abstractions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Common.Logs;

/// <summary>
/// Envuelve una tarea periódica (<c>/internal/jobs/…</c>) para dejar su rastro: una fila en
/// <c>logs.job_runs</c>, el latido del proceso, el error con <c>Source = job</c> si falla y la purga diaria
/// de los logs (que corre dentro de esta misma tarea, como máximo una vez al día).
/// </summary>
public sealed class LoggedJobRunner(
    IClock clock,
    ILogStore logStore,
    ILogMaintenance maintenance,
    ILogger<LoggedJobRunner> logger)
{
    /// <summary>Marca en <see cref="Exception.Data"/> de una excepción que ya quedó en <c>logs.errors</c>.</summary>
    public const string AlreadyLoggedKey = "gsm.error_logged";

    public async Task<T> RunAsync<T>(
        string job,
        Func<Task<T>> work,
        Func<T, (int Processed, int Failed)> count)
    {
        var startedAt = clock.UtcNow;
        try
        {
            var result = await work();
            var (processed, failed) = count(result);
            logStore.RecordJobRun(new JobRunLogEntry(
                job, startedAt, clock.UtcNow,
                failed > 0 ? JobRunStatus.Partial : JobRunStatus.Ok,
                processed, failed, Error: null));
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logStore.RecordError(new ErrorLogEntry(ErrorSource.Job, ex));
            ex.Data[AlreadyLoggedKey] = true;
            logStore.RecordJobRun(new JobRunLogEntry(
                job, startedAt, clock.UtcNow, JobRunStatus.Failed, 0, 0,
                $"{ex.GetType().Name}: {ex.Message}"));
            throw;
        }
        finally
        {
            logStore.RecordHeartbeat(HeartbeatName(job), clock.UtcNow);
            await RunMaintenanceAsync();
        }
    }

    public static string HeartbeatName(string job) => $"{job}_job";

    private async Task RunMaintenanceAsync()
    {
        try
        {
            var purge = await maintenance.RunDailyIfDueAsync();
            if (purge is not null)
            {
                logger.LogInformation(
                    "Purga de logs: {Created} particiones creadas, {Dropped} soltadas, {Rows} filas borradas",
                    purge.PartitionsCreated, purge.PartitionsDropped, purge.RowsDeleted);
            }
        }
        catch (Exception ex)
        {
            // Mantenimiento fallido no tumba la tarea: queda en errors (Source = job) y en el log de archivo.
            logger.LogError(ex, "La purga diaria de logs falló");
            logStore.RecordError(new ErrorLogEntry(ErrorSource.Job, ex));
        }
    }
}
