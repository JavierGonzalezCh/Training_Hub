using GSM.API.Auth;
using GSM.Application.Identity;
using GSM.Application.Identity.Contracts;
using GSM.Application.Gyms.Public;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GSM.API.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType<AuthTokensResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthTokensResponse>> Login(
        LoginRequest request, [FromServices] LoginHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType<AuthTokensResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthTokensResponse>> Refresh(
        RefreshRequest request, [FromServices] RefreshTokenHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(
        LogoutRequest request, [FromServices] LogoutHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(request, cancellationToken);
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<MeResponse>> Me(
        [FromServices] GetMeHandler handler,
        [FromServices] IGymReader gyms,
        CancellationToken cancellationToken)
    {
        var me = await handler.HandleAsync(cancellationToken);

        if (me.GymId is not { } gymId)
        {
            return Ok(me);
        }

        var branding = await gyms.GetBrandingAsync(gymId, cancellationToken);
        var flags = await gyms.GetFeatureFlagsAsync(gymId, cancellationToken);

        return Ok(me with
        {
            Gym = branding is null
                ? null
                : new MeGym(branding.Name, branding.LogoUrl, branding.PrimaryColor, branding.SecondaryColor),
            FeatureFlags = flags is null
                ? null
                : new MeFeatureFlags(
                    flags.ShowRoutines, flags.ShowClasses, flags.ShowProgress, flags.ShowGamification,
                    flags.ShowCommunity, flags.CheckinQr, flags.CommunityComments),
        });
    }
}
