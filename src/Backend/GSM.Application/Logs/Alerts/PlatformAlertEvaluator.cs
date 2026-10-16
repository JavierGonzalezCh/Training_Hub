using GSM.Application.Common.Abstractions;
using GSM.Application.Notifications;
using GSM.Application.Notifications.Public;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Logs.Alerts;

/// <param name="Raised">Alertas nuevas creadas en esta vuelta.</param>
/// <param name="Delivered">Alertas a las que se les intentó la entrega en esta vuelta.</param>
public sealed record PlatformAlertRunResult(int Raised, int Delivered);

/// <summary>
/// Alertas al SuperAdmin (docs/GSM-logs.md §8). Una vuelta mide las tablas de <c>logs</c>, crea las alertas que
/// corresponden y entrega las pendientes por correo y push (vía <see cref="IPlatformAlertNotifier"/>).
/// <para>
/// Lo ejecutan el servicio en segundo plano del dispatcher (cada minuto) y la tarea periódica (en cada ejecución):
/// así, si uno de los dos procesos se detiene, el otro lo detecta por su latido. Es seguro correrlo en varias copias
/// de la API a la vez: la repetición y la entrega se reclaman de forma atómica en la base.
/// </para>
/// <para>
/// <b>Nunca lanza ni deja rastro en <c>logs.errors</c></b>: una falla del propio evaluador solo va al log de archivo,
/// para que una avería de las alertas no genere más alertas. Es idempotente.
/// </para>
/// </summary>
public sealed class PlatformAlertEvaluator(
    PlatformAlertOptions options,
    IAlertSignalSource signals,
    ILogAdminStore adminStore,
    IPlatformAlertStore store,
    IPlatformAlertNotifier notifier,
    IClock clock,
    ILogger<PlatformAlertEvaluator> logger)
{
    private const string LogsLink = "/admin/logs";
    private const int MaxDeliveriesPerRound = 20;
    private static readonly TimeSpan DeliveryHold = TimeSpan.FromMinutes(2);

    /// <param name="skipHeartbeats">
    /// Los procesos que están ejecutando esta vuelta: su latido de esta ejecución aún no se guardó (el log escribe en
    /// segundo plano), así que no se juzgan.
    /// </param>
    public async Task<PlatformAlertRunResult> RunOnceAsync(
        IReadOnlyCollection<string>? skipHeartbeats = null, CancellationToken cancellationToken = default)
    {
        if (!options.Enabled)
        {
            return new PlatformAlertRunResult(0, 0);
        }

        var now = clock.UtcNow;
        var candidates = new List<AlertCandidate>();
        await CollectAsync(candidates, "fallas", () => FailureCandidatesAsync(now, cancellationToken));
        await CollectAsync(candidates, "latidos", () => HeartbeatCandidatesAsync(now, skipHeartbeats, cancellationToken));
        await CollectAsync(candidates, "servicios externos", () => ExternalCandidatesAsync(now, cancellationToken));
        await CollectAsync(candidates, "seguridad", () => SecurityCandidatesAsync(now, cancellationToken));

        var repeat = TimeSpan.FromMinutes(options.RepeatMinutes);
        var raised = 0;
        foreach (var candidate in candidates)
        {
            try
            {
                if (await store.TryRaiseAsync(candidate, now, candidate.Once ? null : repeat, cancellationToken) is not null)
                {
                    raised++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "No se pudo registrar la alerta {Key}", candidate.Key);
            }
        }

        var delivered = await DeliverPendingAsync(now, cancellationToken);
        if (raised > 0 || delivered > 0)
        {
            logger.LogInformation("Alertas al SuperAdmin: {Raised} nuevas, {Delivered} entregas intentadas", raised, delivered);
        }

        return new PlatformAlertRunResult(raised, delivered);
    }

    // ------------------------------------------------------------------ detección

    private async Task<IEnumerable<AlertCandidate>> FailureCandidatesAsync(DateTimeOffset now, CancellationToken ct)
    {
        var ignored = (await adminStore.GetGroupStatesAsync(ct))
            .Where(s => s.Status == ErrorGroupStatuses.Ignored)
            .Select(s => (s.Kind, s.Fingerprint))
            .ToHashSet();
        var result = new List<AlertCandidate>();

        // Error nuevo: un grupo visto hace poco que nunca avisó. Avisa una sola vez por grupo.
        var recent = await signals.GetFailureGroupsSinceAsync(now.AddMinutes(-options.NewErrorLookbackMinutes), ct);
        foreach (var group in recent.Where(g => !ignored.Contains((g.Kind, g.Fingerprint))))
        {
            result.Add(new AlertCandidate(
                $"new:{group.Kind}:{group.Fingerprint}",
                PlatformAlertKinds.NewError,
                Clip($"Falla nueva: {group.Title}"),
                Describe(group, $"Primera ocurrencia reciente de {KindLabel(group.Kind)}."),
                LogsLink,
                Once: true));
        }

        // Pico: más de N ocurrencias del mismo grupo dentro de la ventana corta.
        var window = await signals.GetFailureGroupsSinceAsync(now.AddMinutes(-options.SpikeWindowMinutes), ct);
        foreach (var group in window.Where(g => g.Count > options.SpikeCount && !ignored.Contains((g.Kind, g.Fingerprint))))
        {
            result.Add(new AlertCandidate(
                $"spike:{group.Kind}:{group.Fingerprint}",
                PlatformAlertKinds.Spike,
                Clip($"Pico de fallas: {group.Title}"),
                Describe(group, $"{group.Count} ocurrencias en los últimos {options.SpikeWindowMinutes} minutos."),
                LogsLink,
                Once: false));
        }

        return result;
    }

    private async Task<IEnumerable<AlertCandidate>> HeartbeatCandidatesAsync(
        DateTimeOffset now, IReadOnlyCollection<string>? skip, CancellationToken ct)
    {
        var result = new List<AlertCandidate>();
        foreach (var beat in await signals.GetHeartbeatsAsync(ct))
        {
            if ((skip is not null && skip.Contains(beat.Process)) || DeadlineFor(beat.Process) is not { } deadline)
            {
                continue;
            }

            var silence = now - beat.LastBeatAt;
            if (silence <= deadline)
            {
                continue;
            }

            result.Add(new AlertCandidate(
                $"heartbeat:{beat.Process}",
                PlatformAlertKinds.ProcessStopped,
                Clip($"Proceso detenido: {beat.Process}"),
                $"Su último latido fue hace {(int)silence.TotalMinutes} minutos y el plazo es de {(int)deadline.TotalMinutes}. " +
                "Puede haberse detenido sin dar error.",
                LogsLink,
                Once: false));
        }

        return result;
    }

    private TimeSpan? DeadlineFor(string process)
    {
        if (process == NotificationDispatchRunner.Name)
        {
            return TimeSpan.FromMinutes(options.DispatcherHeartbeatMinutes);
        }

        // Las tareas periódicas (`/internal/jobs/…`) dejan el latido `<tarea>_job`. Otros latidos (la purga) no se juzgan.
        return process.EndsWith("_job", StringComparison.Ordinal)
            ? TimeSpan.FromMinutes(options.JobHeartbeatMinutes)
            : null;
    }

    private async Task<IEnumerable<AlertCandidate>> ExternalCandidatesAsync(DateTimeOffset now, CancellationToken ct)
    {
        // Sin registro de éxitos no se puede saber si las fallas fueron "seguidas": se cuentan las fallas del
        // proveedor dentro de la ventana (decisión conservadora, anotada en la entrega).
        var counts = await signals.GetExternalFailureCountsSinceAsync(now.AddMinutes(-options.ExternalWindowMinutes), ct);
        return counts
            .Where(c => c.Count >= options.ExternalFailureCount)
            .Select(c => new AlertCandidate(
                $"external:{c.Provider}",
                PlatformAlertKinds.ExternalDown,
                Clip($"Servicio externo con fallas: {c.Provider}"),
                $"{c.Count} llamadas fallidas a {c.Provider} en los últimos {options.ExternalWindowMinutes} minutos.",
                LogsLink,
                Once: false));
    }

    private async Task<IEnumerable<AlertCandidate>> SecurityCandidatesAsync(DateTimeOffset now, CancellationToken ct)
    {
        var counts = await signals.GetLoginFailureCountsSinceAsync(now.AddMinutes(-options.LoginWindowMinutes), ct);
        return counts
            .Where(c => c.Count >= options.LoginFailureCount)
            .Select(c => new AlertCandidate(
                $"security:login:{c.Ip}",
                PlatformAlertKinds.Security,
                "Ráfaga de logins fallidos",
                $"{c.Count} logins fallidos desde la IP {c.Ip} en los últimos {options.LoginWindowMinutes} minutos.",
                LogsLink,
                Once: false));
    }

    // ------------------------------------------------------------------ entrega

    private async Task<int> DeliverPendingAsync(DateTimeOffset now, CancellationToken ct)
    {
        IReadOnlyList<PlatformAlertRecord> due;
        try
        {
            due = await store.ListDueForDeliveryAsync(now, MaxDeliveriesPerRound, options.MaxDeliveryAttempts, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "No se pudieron listar las alertas pendientes de entrega");
            return 0;
        }

        var attempted = 0;
        foreach (var alert in due)
        {
            try
            {
                if (!await store.TryBeginDeliveryAsync(alert.Id, now, DeliveryHold, options.MaxDeliveryAttempts, ct))
                {
                    continue; // otra copia de la API la tomó
                }

                attempted++;
                var result = await notifier.DeliverAsync(
                    new PlatformAlertMessage(alert.Id, alert.Title, alert.Body, alert.Link),
                    sendEmail: alert.EmailStatus == PlatformAlertStatuses.Pending,
                    sendPush: alert.PushStatus == PlatformAlertStatuses.Pending,
                    ct);

                await store.CompleteDeliveryAsync(
                    alert.Id,
                    Merge(alert.EmailStatus, result.Email),
                    Merge(alert.PushStatus, result.Push),
                    result.Error,
                    options.MaxDeliveryAttempts,
                    ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Fallo la entrega de la alerta {AlertId}", alert.Id);
            }
        }

        return attempted;
    }

    private static string Merge(string current, PlatformAlertChannelResult result) => result switch
    {
        PlatformAlertChannelResult.Sent => PlatformAlertStatuses.Sent,
        PlatformAlertChannelResult.Skipped => PlatformAlertStatuses.Skipped,
        PlatformAlertChannelResult.Failed => PlatformAlertStatuses.Failed,
        _ => current,
    };

    // ------------------------------------------------------------------ utilidades

    private async Task CollectAsync(
        List<AlertCandidate> into, string what, Func<Task<IEnumerable<AlertCandidate>>> detect)
    {
        try
        {
            into.AddRange(await detect());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Una medición que falla no impide las demás ni tumba la vuelta.
            logger.LogError(ex, "No se pudo evaluar las alertas de {What}", what);
        }
    }

    private static string Describe(RecentFailureGroup group, string lead)
        => string.IsNullOrWhiteSpace(group.Detail) ? lead : $"{lead}\nDónde: {group.Detail}";

    private static string KindLabel(string kind)
        => kind == ErrorGroupKinds.EventFailure ? "un evento fallido" : "un error";

    private static string Clip(string text) => text.Length > 200 ? text[..200] : text;
}
