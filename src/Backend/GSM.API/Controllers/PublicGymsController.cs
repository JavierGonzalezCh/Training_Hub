using GSM.API.Auth;
using GSM.API.Filters;
using GSM.Application.Identity;
using GSM.Application.Identity.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GSM.API.Controllers;

/// <summary>Datos públicos de un gym para pantallas sin sesión (login del miembro con la marca del gym).</summary>
[ApiController]
[Route("api/public/gyms")]
[AllowAnonymous]
[SubscriptionGateExempt]
public sealed class PublicGymsController : ControllerBase
{
    /// <summary>Nombre, logo y colores del gym por su slug. 404 si no existe.</summary>
    [HttpGet("{slug}/branding")]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType<PublicGymBrandingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PublicGymBrandingResponse>> Branding(
        string slug, [FromServices] GetPublicGymBrandingHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(slug, cancellationToken));
}
