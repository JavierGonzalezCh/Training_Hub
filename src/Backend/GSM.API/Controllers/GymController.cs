using GSM.API.Auth;
using GSM.Application.Gyms;
using GSM.Application.Gyms.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>Gestión del gym del usuario autenticado (sin id en la ruta: siempre el del token).</summary>
[ApiController]
[Route("api/gym")]
[Authorize(Policy = AuthPolicies.RequireStaff)]
public sealed class GymController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<GymResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GymResponse>> Get(
        [FromServices] GetGymHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    [HttpPut]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [ProducesResponseType<GymResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GymResponse>> Update(
        UpdateGymRequest request, [FromServices] UpdateGymHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));

    [HttpGet("feature-flags")]
    [ProducesResponseType<GymFeatureFlagsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GymFeatureFlagsResponse>> GetFeatureFlags(
        [FromServices] GetFeatureFlagsHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    [HttpPut("feature-flags")]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [ProducesResponseType<GymFeatureFlagsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GymFeatureFlagsResponse>> UpdateFeatureFlags(
        UpdateFeatureFlagsRequest request,
        [FromServices] UpdateFeatureFlagsHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));
}
