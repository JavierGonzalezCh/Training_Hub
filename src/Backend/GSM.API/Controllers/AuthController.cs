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

    /// <summary>
    /// "Enviarme un código": manda al correo un código de 6 dígitos (vence en 10 minutos, un solo uso, 5 intentos;
    /// uno por minuto y 5 por hora por persona). Responde 202 igual exista o no la cuenta del miembro.
    /// </summary>
    [HttpPost("request-login-code")]
    [AllowAnonymous]
    [SubscriptionGateExempt]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> RequestLoginCode(
        RequestLoginCodeRequest request,
        [FromServices] RequestLoginCodeHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(request, cancellationToken);
        return Accepted();
    }

    /// <summary>
    /// Entra con el código del correo (solo accesos de miembro). Con un solo gym deja la sesión iniciada; con
    /// varios, la persona elige como en el login. 401 si el código no es válido, venció o se agotó.
    /// </summary>
    [HttpPost("verify-login-code")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<LoginResponse>> VerifyLoginCode(
        VerifyLoginCodeRequest request,
        [FromServices] VerifyLoginCodeHandler handler,
        CancellationToken cancellationToken)
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

    /// <summary>El equipo del kiosco canjea el código de emparejamiento y recibe su sesión de dispositivo (rol kiosk).</summary>
    [HttpPost("kiosk/pair")]
    [AllowAnonymous]
    [SubscriptionGateExempt]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType<AuthTokensResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuthTokensResponse>> PairKiosk(
        PairKioskRequest request, [FromServices] PairKioskHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));

    /// <summary>Renueva el access token del kiosco con su token de renovación (no rota). 401 si el Owner lo revocó.</summary>
    [HttpPost("kiosk/refresh")]
    [AllowAnonymous]
    [SubscriptionGateExempt]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType<AuthTokensResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuthTokensResponse>> RefreshKiosk(
        KioskRefreshRequest request, [FromServices] RefreshKioskHandler handler, CancellationToken cancellationToken)
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

    /// <summary>
    /// Pide cambiar el email de la cuenta (cualquier rol): manda un código de 6 dígitos al correo NUEVO (vence en
    /// 10 minutos, un solo uso, 5 intentos; uno por minuto y 5 por hora). El email no cambia hasta confirmarlo.
    /// 409 si el email ya lo usa otra cuenta o se pidieron demasiados códigos; 422 si es el mismo email.
    /// </summary>
    [HttpPost("~/api/me/request-email-change")]
    [Authorize]
    [SubscriptionGateExempt]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RequestEmailChange(
        RequestEmailChangeRequest request,
        [FromServices] RequestEmailChangeHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(request, cancellationToken);
        return Accepted();
    }

    /// <summary>
    /// Confirma el cambio de email con el código del correo nuevo. Actualiza el email de la cuenta y de todas las
    /// fichas de miembro vinculadas a ella, en todos sus gyms. 422 si el código no es válido, venció o se agotó.
    /// </summary>
    [HttpPost("~/api/me/confirm-email-change")]
    [Authorize]
    [SubscriptionGateExempt]
    [ProducesResponseType<EmailChangedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<EmailChangedResponse>> ConfirmEmailChange(
        ConfirmEmailChangeRequest request,
        [FromServices] ConfirmEmailChangeHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));

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
