using GSM.API.Auth;
using GSM.API.Filters;
using GSM.Application.Common.Logs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GSM.API.Controllers;

/// <summary>Reporte de errores del cliente (docs/GSM-logs.md §4.6). Lo consulta solo el SuperAdmin (panel en LG4).</summary>
[ApiController]
[Route("api/logs")]
[AllowAnonymous]
[SubscriptionGateExempt]
public sealed class LogsController : ControllerBase
{
    /// <summary>
    /// Guarda un error no controlado del navegador o de la app. Acepta envíos sin sesión (un error en el login
    /// también cuenta); con límite de envíos por IP (429) y de tamaño (400 por campo, 413 el cuerpo entero).
    /// </summary>
    [HttpPost("client")]
    [EndpointName("ReportClientError")]
    [EnableRateLimiting(RateLimitPolicies.ClientErrors)]
    [MaxContentLength(ClientErrorLimits.MaxBodyBytes)]
    [RequestSizeLimit(ClientErrorLimits.MaxBodyBytes)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ReportClientError(
        ClientErrorRequest request,
        [FromServices] ReportClientErrorHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(request, cancellationToken);
        return NoContent();
    }
}
