using GSM.Client.Errors;
using GSM.Client.Generated;
using GSM.Client.Http;

namespace GSM.UI.Shared.Auth;

/// <summary>
/// Orquesta la sesión: login, logout, bootstrap (<c>/api/auth/me</c>) y el flujo de contraseña
/// olvidada. Traduce los errores del cliente generado a <see cref="ApiError"/>.
/// </summary>
public sealed class SessionService(
    IAuthClient auth,
    ITokenStore tokenStore,
    GsmAuthenticationStateProvider authState,
    GymContext gymContext)
{
    /// <summary>Login + bootstrap. Devuelve el <see cref="MeResponse"/> para redirigir por rol.</summary>
    public async Task<MeResponse> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        AuthTokensResponse tokens;
        try
        {
            tokens = await auth.LoginAsync(new LoginRequest { Email = email, Password = password }, cancellationToken);
        }
        catch (GsmApiException ex)
        {
            throw new ApiErrorException(ApiError.FromException(ex));
        }

        await tokenStore.SetAsync(
            new AuthTokens(tokens.AccessToken, tokens.RefreshToken, tokens.ExpiresAtUtc), cancellationToken);
        authState.NotifyChanged();

        return await BootstrapAsync(cancellationToken);
    }

    /// <summary>Relee <c>/api/auth/me</c> y refresca el <see cref="GymContext"/>.</summary>
    public async Task<MeResponse> BootstrapAsync(CancellationToken cancellationToken = default)
    {
        MeResponse me;
        try
        {
            me = await auth.MeAsync(cancellationToken);
        }
        catch (GsmApiException ex)
        {
            throw new ApiErrorException(ApiError.FromException(ex));
        }

        var tokens = await tokenStore.GetAsync(cancellationToken);
        var impersonating = JwtPayload.TryRead(tokens?.AccessToken)?.Impersonating ?? false;
        gymContext.Set(me, impersonating);
        return me;
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var tokens = await tokenStore.GetAsync(cancellationToken);
        if (tokens is not null)
        {
            try
            {
                await auth.LogoutAsync(new LogoutRequest { RefreshToken = tokens.RefreshToken }, cancellationToken);
            }
            catch (GsmApiException)
            {
                // El logout es best-effort: si la API falla, igualmente limpiamos localmente.
            }
        }

        await tokenStore.ClearAsync(cancellationToken);
        gymContext.Clear();
        authState.NotifyChanged();
    }

    public async Task ForgotPasswordAsync(string email, CancellationToken cancellationToken = default)
    {
        try
        {
            await auth.ForgotPasswordAsync(new ForgotPasswordRequest { Email = email }, cancellationToken);
        }
        catch (GsmApiException ex)
        {
            throw new ApiErrorException(ApiError.FromException(ex));
        }
    }

    public async Task ResetPasswordAsync(
        string email, string token, string newPassword, CancellationToken cancellationToken = default)
    {
        try
        {
            await auth.ResetPasswordAsync(
                new ResetPasswordRequest { Email = email, Token = token, NewPassword = newPassword }, cancellationToken);
        }
        catch (GsmApiException ex)
        {
            throw new ApiErrorException(ApiError.FromException(ex));
        }
    }
}
