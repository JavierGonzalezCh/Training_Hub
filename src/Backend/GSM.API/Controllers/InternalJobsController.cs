using System.Security.Cryptography;
using System.Text;
using GSM.API.Filters;
using GSM.Application.Common.Logs;
using GSM.Application.Communications;
using GSM.Application.Communications.Contracts;
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

    /// <summary>Recordatorios de vencimiento, envío de la cola de correos y purga de avisos viejos.</summary>
    [HttpPost("communications/run")]
    public async Task<ActionResult<CommunicationsJobResult>> RunCommunications(
        [FromQuery] int? batchSize,
        [FromServices] RunCommunicationsJobHandler handler,
        [FromServices] LoggedJobRunner runner,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorized())
        {
            return Unauthorized();
        }

        var result = await runner.RunAsync(
            "communications",
            () => handler.HandleAsync(batchSize ?? RunCommunicationsJobHandler.DefaultBatchSize, cancellationToken),
            r => (r.RemindersCreated + r.EmailsSent + r.EmailsFailed, r.EmailsFailed));
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
