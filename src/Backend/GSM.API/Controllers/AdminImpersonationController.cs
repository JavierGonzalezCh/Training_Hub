using GSM.API.Auth;
using GSM.API.Filters;
using GSM.Application.SuperAdmin;
using GSM.Application.SuperAdmin.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>
/// Impersonación de solo lectura para soporte. Exento del filtro de estado para que
/// <c>end</c> sea siempre alcanzable, incluso con el propio token de impersonación.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Policy = AuthPolicies.RequireSuperAdmin)]
[SubscriptionGateExempt]
public sealed class AdminImpersonationController : ControllerBase
{
    /// <summary>Abre una sesión y devuelve un token corto (15 min). Motivo obligatorio → 422 si falta.</summary>
    [HttpPost("impersonate/{gymId:guid}")]
    [ProducesResponseType<ImpersonationTokenResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ImpersonationTokenResponse>> Open(
        Guid gymId,
        OpenImpersonationRequest request,
        [FromServices] OpenImpersonationHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(gymId, request, cancellationToken);
        return Created((string?)null, result);
    }

    /// <summary>Cierra la sesión de impersonación en curso.</summary>
    [HttpPost("impersonate/end")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> End(
        [FromServices] EndImpersonationHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Historial de auditoría. Filtro opcional por gym.</summary>
    [HttpGet("impersonations")]
    [ProducesResponseType<IReadOnlyList<ImpersonationItem>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ImpersonationItem>>> History(
        [FromQuery] Guid? gymId,
        [FromQuery] int limit,
        [FromServices] ListImpersonationsHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(gymId, limit, cancellationToken));
}
