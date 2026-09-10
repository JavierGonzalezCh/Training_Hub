using GSM.API.Auth;
using GSM.API.Uploads;
using GSM.Application.Gyms;
using GSM.Application.Gyms.Contracts;
using GSM.Application.SuperAdmin.Public;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>Gestión del gym del usuario autenticado (sin id en la ruta: siempre el del token).</summary>
[ApiController]
[Route("api/gym")]
[Authorize(Policy = AuthPolicies.RequireStaff)]
public sealed class GymController : ControllerBase
{
    /// <summary>
    /// Datos del gym más el bloque <c>subscription</c>, que se compone aquí y no en el caso de uso
    /// para que Gyms no dependa de SuperAdmin (mismo patrón que <c>GET /api/auth/me</c>).
    /// </summary>
    [HttpGet]
    [ProducesResponseType<GymResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GymResponse>> Get(
        [FromServices] GetGymHandler handler,
        [FromServices] ISubscriptionReader subscriptions,
        CancellationToken cancellationToken)
    {
        var gym = await handler.HandleAsync(cancellationToken);
        var view = await subscriptions.GetStatusViewAsync(gym.Id, cancellationToken);
        return Ok(gym with { Subscription = view?.ToInfo() });
    }

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

    [HttpPost("logo")]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<GymResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GymResponse>> UploadLogo(
        IFormFile file, [FromServices] UploadGymLogoHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(file.ToFileToStore(), cancellationToken));
}
