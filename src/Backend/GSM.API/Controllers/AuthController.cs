using GSM.API.Auth;
using GSM.API.Filters;
using GSM.Application.Identity;
using GSM.Application.Identity.Contracts;
using GSM.Application.Gyms.Public;
using GSM.Application.SuperAdmin.Public;
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
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<LoginResponse>> Login(
        LoginRequest request, [FromServices] LoginHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));

    /// <summary>Segundo paso del login con varios gyms: con el boleto del login y el acceso elegido emite la sesión.</summary>
    [HttpPost("select-gym")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType<AuthTokensResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuthTokensResponse>> SelectGym(
        SelectGymRequest request, [FromServices] SelectGymHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));

    /// <summary>Cambiar de gym, o pasar al otro acceso del mismo gym (modo staff/miembro), sin pedir contraseña.</summary>
    [HttpPost("switch-gym")]
    [Authorize]
    [SubscriptionGateExempt]
    [ProducesResponseType<AuthTokensResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuthTokensResponse>> SwitchGym(
        SwitchGymRequest request, [FromServices] SwitchGymHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));

    /// <summary>Los accesos activos de la persona (gym, logo, rol), el último usado primero; marca el actual.</summary>
    [HttpGet("accesses")]
    [Authorize]
    [ProducesResponseType<IReadOnlyList<GymAccessOption>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<GymAccessOption>>> Accesses(
        [FromServices] ListMyAccessesHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    [HttpPost("refresh")]
    [AllowAnonymous]
    [SubscriptionGateExempt]
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

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ForgotPassword(
        ForgotPasswordRequest request,
        [FromServices] ForgotPasswordHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(request, cancellationToken);
        return Accepted();
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ResetPassword(
        ResetPasswordRequest request,
        [FromServices] ResetPasswordHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(request, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Activar una cuenta invitada con el enlace <c>/activar?email=…&amp;code=…</c> (72 h): crea la contraseña y
    /// deja la sesión iniciada; con varios accesos, la persona elige gym como en el login. 422 si el enlace
    /// venció, ya se usó o la contraseña no cumple la política.
    /// </summary>
    [HttpPost("activate")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<LoginResponse>> Activate(
        ActivateAccountRequest request,
        [FromServices] ActivateAccountHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));

    /// <summary>
    /// Cambiar la contraseña con la sesión iniciada (cualquier rol). Cierra las demás sesiones de la persona en
    /// todos sus gyms: se conserva la del <c>refreshToken</c> que manda el cliente. 422 si la contraseña
    /// actual es incorrecta.
    /// </summary>
    [HttpPost("~/api/me/password")]
    [Authorize]
    [SubscriptionGateExempt]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ChangePassword(
        ChangePasswordRequest request,
        [FromServices] ChangePasswordHandler handler,
        CancellationToken cancellationToken)
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
        [FromServices] ISubscriptionReader subscriptions,
        CancellationToken cancellationToken)
    {
        var me = await handler.HandleAsync(cancellationToken);

        if (me.GymId is not { } gymId)
        {
            return Ok(me);
        }

        var branding = await gyms.GetBrandingAsync(gymId, cancellationToken);
        var flags = await gyms.GetFeatureFlagsAsync(gymId, cancellationToken);
        var subscription = (await subscriptions.GetStatusViewAsync(gymId, cancellationToken))?.ToInfo();

        return Ok(me with
        {
            Subscription = subscription,
            Gym = branding is null
                ? null
                : new MeGym(branding.Name, branding.LogoUrl, branding.PrimaryColor, branding.SecondaryColor),
            FeatureFlags = flags is null
                ? null
                : new MeFeatureFlags(
                    flags.ShowRoutines, flags.ShowClasses, flags.ShowProgress, flags.ShowGamification,
                    flags.ShowCommunity, flags.CheckinQr, flags.CommunityComments,
                    flags.MembersCanCreateRoutines),
        });
    }
}
