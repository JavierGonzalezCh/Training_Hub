using System.Security.Cryptography;
using System.Text;
using GSM.API.Filters;
using GSM.Application.Common.Logs;
using GSM.Application.Communications;
using GSM.Application.Communications.Contracts;
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
        [FromServices] LoggedJobRunner runner,
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
                    outcome.Reminders, await purge.HandleAsync(cancellationToken), outcome.Birthdays);
            },
            r => (r.RemindersPublished + r.NotificationsPurged + r.BirthdaysPublished, 0));

        // Foto mensual del SaaS: solo hace algo en la primera ejecucion de cada mes (idempotente por mes).
        await runner.RunAsync(
            "monthly_metrics",
            () => monthlyMetrics.HandleAsync(cancellationToken),
            created => (created, 0));
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
