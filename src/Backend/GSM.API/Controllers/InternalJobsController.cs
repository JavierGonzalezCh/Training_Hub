using System.Security.Cryptography;
using System.Text;
using GSM.API.Filters;
using GSM.Application.Classes;
using GSM.Application.Common.Logs;
using GSM.Application.Communications;
using GSM.Application.Communications.Contracts;
using GSM.Application.Logs.Alerts;
using GSM.Application.Members;
using GSM.Application.Notifications;
using GSM.Application.SuperAdmin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>
/// Tareas periódicas del sistema, invocadas por una tarea programada de Dokploy (no hay scheduler
/// dentro de la API: con dos réplicas se ejecutaría dos veces). Se autentica con la cabecera
/// <c>X-Job-Secret</c> contra <c>Jobs:Secret</c>, sin JWT, y no aparece en OpenAPI.
/// </summary>
[ApiController]
[Route("internal/jobs")]
[AllowAnonymous]
[SubscriptionGateExempt]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class InternalJobsController(IConfiguration configuration) : ControllerBase
{
    public const string SecretHeader = "X-Job-Secret";

    /// <summary>
    /// Recordatorios de vencimiento (como eventos, que Notifications convierte en avisos) y purga de avisos
    /// viejos. El envío de correos ya no va aquí: lo hace el dispatcher de Notifications cada 30 segundos.
    /// </summary>
    [HttpPost("communications/run")]
    public async Task<ActionResult<CommunicationsJobResult>> RunCommunications(
        [FromServices] RunCommunicationsJobHandler handler,
        [FromServices] PurgeNotificationsHandler purge,
        [FromServices] RunMonthlyMetricsHandler monthlyMetrics,
        [FromServices] RunSubscriptionNoticesHandler subscriptionNotices,
        [FromServices] GSM.Application.Achievements.CloseRankingMonthHandler rankingClose,
        [FromServices] RunWaitlistJobHandler waitlistJob,
        [FromServices] RunAnonymizationJobHandler anonymizationJob,
        [FromServices] LoggedJobRunner runner,
        [FromServices] PlatformAlertEvaluator alerts,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorized())
        {
            return Unauthorized();
        }

        var result = await runner.RunAsync(
            "communications",
            async () =>
            {
                var outcome = await handler.HandleAsync(cancellationToken);
                return new CommunicationsJobResult(
                    outcome.Reminders, await purge.HandleAsync(cancellationToken), outcome.Birthdays, outcome.Announcements);
            },
            r => (r.RemindersPublished + r.NotificationsPurged + r.BirthdaysPublished + r.AnnouncementsPublished, 0));

        // C4: vence las ofertas de la lista de espera y pasa el cupo al siguiente (idempotente).
        await runner.RunAsync(
            "waitlist",
            () => waitlistJob.HandleAsync(cancellationToken),
            r => (r.OffersMade + r.OffersExpired, 0));

        // M6: aviso a los Owners y anonimizacion de los miembros dados de baja cuyo plazo vencio (idempotente).
        await runner.RunAsync(
            "member_anonymization",
            () => anonymizationJob.HandleAsync(cancellationToken),
            r => (r.Notices + r.Anonymized, 0));

        // S3: avisos de la suscripcion a los Owners (prueba, vencimiento, mora, limite de miembros). Publica eventos
        // idempotentes por hito; Notifications crea los avisos una sola vez.
        await runner.RunAsync(
            "subscription_notices",
            () => subscriptionNotices.HandleAsync(cancellationToken),
            published => (published, 0));

        // AC1: cierre del mes anterior del ranking y logros de ranking (idempotente por gym y mes).
        await runner.RunAsync(
            "ranking_close",
            () => rankingClose.HandleAsync(cancellationToken),
            r => (r.Results, 0));

        // Foto mensual del SaaS: solo hace algo en la primera ejecucion de cada mes (idempotente por mes).
        await runner.RunAsync(
            "monthly_metrics",
            () => monthlyMetrics.HandleAsync(cancellationToken),
            created => (created, 0));

        // LG5: alertas al SuperAdmin. Esta tarea es la que vigila al dispatcher (y viceversa); no lanza.
        // Los latidos de esta misma ejecución todavía se están escribiendo, así que no se juzgan.
        await alerts.RunOnceAsync(
            [LoggedJobRunner.HeartbeatName("communications"), LoggedJobRunner.HeartbeatName("waitlist"), LoggedJobRunner.HeartbeatName("member_anonymization"),
             LoggedJobRunner.HeartbeatName("monthly_metrics")],
            cancellationToken);
        return Ok(result);
    }

    private bool IsAuthorized()
    {
        var expected = configuration["Jobs:Secret"];
        if (string.IsNullOrEmpty(expected) || !Request.Headers.TryGetValue(SecretHeader, out var provided))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided.ToString()), Encoding.UTF8.GetBytes(expected));
    }
}
