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
    /// <summary>
    /// Login. Con un solo gym entra directo (<see cref="LoginOutcome.Me"/>). Con varios no hay sesión
    /// todavía: devuelve la selección (<see cref="LoginOutcome.Selection"/>) y hay que llamar a
    /// <see cref="SelectGymAsync"/> con el acceso elegido.
    /// </summary>
    public async Task<LoginOutcome> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        LoginResponse response;
        try
        {
            response = await auth.LoginAsync(new LoginRequest { Email = email, Password = password }, cancellationToken);
        }
        catch (GsmApiException ex)
        {
            throw new ApiErrorException(ApiError.FromException(ex));
        }

        return await ToOutcomeAsync(response, cancellationToken);
    }

    /// <summary>
    /// Activa una cuenta invitada con el enlace <c>/activar?email=…&amp;code=…</c>: crea la contraseña y deja la
    /// sesión iniciada, o la selección de gym si la persona tiene varios accesos (igual que el login).
    /// </summary>
    public async Task<LoginOutcome> ActivateAsync(
        string email, string code, string newPassword, CancellationToken cancellationToken = default)
    {
        LoginResponse response;
        try
        {
            response = await auth.ActivateAsync(
                new ActivateAccountRequest { Email = email, Code = code, NewPassword = newPassword }, cancellationToken);
        }
        catch (GsmApiException ex)
        {
            throw new ApiErrorException(ApiError.FromException(ex));
        }

        return await ToOutcomeAsync(response, cancellationToken);
    }

    private async Task<LoginOutcome> ToOutcomeAsync(LoginResponse response, CancellationToken cancellationToken)
    {
        if (response.RequiresGymSelection)
        {
            return new LoginOutcome(null, new GymSelection(response.SelectionToken!, response.Accesses!.ToList()));
        }

        return new LoginOutcome(await StartSessionAsync(response.Tokens!, cancellationToken), null);
    }

    /// <summary>
    /// Cambia la contraseña de la sesión actual. El servidor cierra las demás sesiones de la persona y conserva
    /// la que identifica el refresh token que va en la petición, así que antes se fuerza una llamada
    /// autenticada: si el access token venció, el refresh (que rota el token) ocurre ahora y no a mitad del
    /// cambio, cuando el cuerpo ya llevaría un refresh token revocado.
    /// </summary>
    public async Task ChangePasswordAsync(
        string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        try
        {
            await auth.MeAsync(cancellationToken);
            var tokens = await tokenStore.GetAsync(cancellationToken);
            await auth.PasswordAsync(
                new ChangePasswordRequest
                {
                    CurrentPassword = currentPassword,
                    NewPassword = newPassword,
                    RefreshToken = tokens?.RefreshToken,
                },
                cancellationToken);
        }
        catch (GsmApiException ex)
        {
            throw new ApiErrorException(ApiError.FromException(ex));
        }
    }

    /// <summary>Segundo paso del login con varios gyms: con el boleto y el acceso elegido abre la sesión.</summary>
    public async Task<MeResponse> SelectGymAsync(
        GymSelection selection, Guid accessId, CancellationToken cancellationToken = default)
    {
        AuthTokensResponse tokens;
        try
        {
            tokens = await auth.SelectGymAsync(
                new SelectGymRequest { SelectionToken = selection.Ticket, AccessId = accessId }, cancellationToken);
        }
        catch (GsmApiException ex)
        {
            throw new ApiErrorException(ApiError.FromException(ex));
        }

        return await StartSessionAsync(tokens, cancellationToken);
    }

    /// <summary>
    /// Guarda los tokens de una sesión recién emitida (login, registro del miembro, cambio de gym) y
    /// carga el contexto del gym.
    /// </summary>
    public async Task<MeResponse> StartSessionAsync(AuthTokensResponse tokens, CancellationToken cancellationToken = default)
    {
        await tokenStore.SetAsync(
            new AuthTokens(tokens.AccessToken, tokens.RefreshToken, tokens.ExpiresAtUtc), cancellationToken);
        authState.NotifyChanged();

        return await BootstrapAsync(cancellationToken);
    }

    /// <summary>Los accesos de la persona (gym y rol), para "Cambiar de gym". Vacío si falla o no tiene.</summary>
    public async Task<IReadOnlyList<GymAccessOption>> GetAccessesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return (await auth.AccessesAsync(cancellationToken)).ToList();
        }
        catch (GsmApiException)
        {
            return [];
        }
    }

    /// <summary>
    /// Cambia a otro acceso (otro gym, o el otro modo del mismo gym) sin pedir contraseña. Quien llama
    /// debe recargar la app (<c>forceLoad</c>) para descartar el estado del gym anterior.
    /// </summary>
    public async Task<MeResponse> SwitchGymAsync(Guid accessId, CancellationToken cancellationToken = default)
    {
        var current = await tokenStore.GetAsync(cancellationToken);
        AuthTokensResponse tokens;
        try
        {
            tokens = await auth.SwitchGymAsync(
                new SwitchGymRequest { AccessId = accessId, RefreshToken = current?.RefreshToken }, cancellationToken);
        }
        catch (GsmApiException ex)
        {
            throw new ApiErrorException(ApiError.FromException(ex));
        }

        return await StartSessionAsync(tokens, cancellationToken);
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

/// <summary>Resultado del login: o ya hay sesión (<see cref="Me"/>) o falta elegir gym (<see cref="Selection"/>).</summary>
public sealed record LoginOutcome(MeResponse? Me, GymSelection? Selection);

/// <summary>Boleto del login y los accesos entre los que elegir (el último usado, primero).</summary>
public sealed record GymSelection(string Ticket, IReadOnlyList<GymAccessOption> Accesses);
