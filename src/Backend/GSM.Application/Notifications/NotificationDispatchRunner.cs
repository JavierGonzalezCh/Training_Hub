using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Logs;
using GSM.Application.Notifications.Abstractions;
using GSM.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Notifications;

/// <param name="LockAcquired"><c>false</c> si otra copia de la API tenía el candado y esta vuelta no hizo nada.</param>
/// <param name="Processed">Envíos intentados más avisos cerrados sin envío.</param>
/// <param name="Failed">Envíos que agotaron sus intentos en esta vuelta.</param>
public sealed record DispatchRoundResult(bool LockAcquired, int Processed, int Failed);

/// <summary>
/// Una vuelta del <c>NotificationDispatcher</c> (spec §5). Toma el candado de Postgres, procesa los
/// avisos pendientes cuya hora llegó y envía sus correos; reintenta los fallidos hasta 3 veces con espera
/// creciente. Corre sin usuario (Global Query Filter desactivado: ve todos los gyms).
/// <para>
/// Deja el latido <c>notifications_dispatcher</c> en cada vuelta y una fila en <c>logs.job_runs</c> solo
/// cuando procesó algo (o falló). Es la unidad que ejecutan el servicio en segundo plano y los tests.
/// </para>
/// </summary>
public sealed class NotificationDispatchRunner(
    INotificationRepository notifications,
    INotificationDispatchLock dispatchLock,
    IEmailSender emailSender,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogStore logStore,
    ILogger<NotificationDispatchRunner> logger)
{
    public const string Name = "notifications_dispatcher";

    public const int BatchSize = 100;

    public async Task<DispatchRoundResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var startedAt = clock.UtcNow;
        try
        {
            var held = await dispatchLock.TryAcquireAsync(cancellationToken);
            if (held is null)
            {
                return new DispatchRoundResult(false, 0, 0);
            }

            await using var lockScope = held;

            var (processed, failed) = await ProcessAsync(cancellationToken);
            if (processed > 0)
            {
                logStore.RecordJobRun(new JobRunLogEntry(
                    Name, startedAt, clock.UtcNow,
                    failed > 0 ? JobRunStatus.Partial : JobRunStatus.Ok,
                    processed, failed, Error: null));
            }

            return new DispatchRoundResult(true, processed, failed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logStore.RecordError(new ErrorLogEntry(ErrorSource.Dispatcher, ex));
            logStore.RecordJobRun(new JobRunLogEntry(
                Name, startedAt, clock.UtcNow, JobRunStatus.Failed, 0, 0, $"{ex.GetType().Name}: {ex.Message}"));
            throw;
        }
        finally
        {
            logStore.RecordHeartbeat(Name, clock.UtcNow);
        }
    }

    private async Task<(int Processed, int Failed)> ProcessAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var due = await notifications.GetDueAsync(now, BatchSize, cancellationToken);
        if (due.Count == 0)
        {
            return (0, 0);
        }

        var deliveries = (await notifications.GetDeliveriesAsync(due.Select(n => n.Id).ToList(), cancellationToken))
            .ToLookup(d => d.NotificationId);

        var processed = 0;
        var failed = 0;
        foreach (var notification in due)
        {
            var mine = deliveries[notification.Id].ToList();
            foreach (var delivery in mine.Where(d =>
                         d.Status == DeliveryStatus.Pending && (d.NextAttemptAt is null || d.NextAttemptAt <= now)))
            {
                processed++;
                try
                {
                    await emailSender.SendAsync(
                        new EmailMessage(delivery.ToAddress, delivery.Subject, delivery.HtmlBody, delivery.TextBody),
                        cancellationToken);
                    delivery.MarkSent(now);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    delivery.MarkAttemptFailed(ex.Message, now);
                    if (delivery.Status == DeliveryStatus.Failed)
                    {
                        failed++;
                    }

                    logger.LogWarning(
                        ex, "Fallo al enviar el correo {DeliveryId} (intento {Attempt})", delivery.Id, delivery.Attempts);
                }

                // Se guarda tras cada envío: si la vuelta se corta, no se reenvía lo ya enviado.
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            if (mine.Any(d => d.Status == DeliveryStatus.Pending))
            {
                continue; // quedan reintentos esperando su hora
            }

            if (mine.Count == 0)
            {
                processed++;
            }

            if (mine.Count > 0 && mine.All(d => d.Status == DeliveryStatus.Failed))
            {
                notification.MarkFailed();
            }
            else
            {
                notification.MarkSent(now);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation(
            "Dispatcher de avisos: {Processed} procesados, {Failed} correos fallidos", processed, failed);
        return (processed, failed);
    }
}
