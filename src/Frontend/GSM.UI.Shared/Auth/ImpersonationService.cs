using GSM.Client.Generated;
using GSM.Client.Http;
using Microsoft.AspNetCore.Components;

namespace GSM.UI.Shared.Auth;

/// <summary>
/// Abre y cierra una sesión de impersonación, de lectura o de escritura (S6). Mientras dura, los tokens propios
/// del superadmin se guardan aparte (<see cref="ITokenStore.BackupAsync"/>) y se restauran al
/// terminar. El token de impersonación no tiene refresh (15 min fijos): si expira, el
/// <c>AuthenticatedHttpHandler</c> intenta refrescar con un refresh token vacío, falla, limpia la
/// sesión y el usuario vuelve al login — aceptable para una sesión de soporte tan corta.
/// </summary>
public sealed class ImpersonationService(
    IAdminImpersonationClient impersonation,
    ITokenStore tokenStore,
    GymContext gymContext,
    NavigationManager navigation)
{
    public const string ReadMode = "read";
    public const string WriteMode = "write";

    public async Task StartAsync(
        Guid gymId, string reason, string mode = ReadMode, CancellationToken cancellationToken = default)
    {
        var current = await tokenStore.GetAsync(cancellationToken);
        if (current is not null)
        {
            await tokenStore.BackupAsync(current, cancellationToken);
        }

        var token = await impersonation.ImpersonateAsync(gymId, new OpenImpersonationRequest { Reason = reason, Mode = mode }, cancellationToken);

        await tokenStore.SetAsync(new AuthTokens(token.Token, RefreshToken: string.Empty, token.ExpiresAtUtc), cancellationToken);
        gymContext.Clear();

        // forceLoad: un recorrido en memoria puede evaluar la autorización de la ruta destino
        // antes de que GsmAuthenticationStateProvider termine de recomputar el estado con los
        // tokens ya guardados; una recarga completa relee siempre desde el almacén ya asentado.
        navigation.NavigateTo("/", forceLoad: true);
    }

    public async Task EndAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await impersonation.EndAsync(cancellationToken);
        }
        catch (GsmApiException)
        {
            // Best-effort: si el token ya expiró, igual restauramos la sesión propia.
        }

        var backup = await tokenStore.GetBackupAsync(cancellationToken);
        if (backup is not null)
        {
            await tokenStore.SetAsync(backup, cancellationToken);
            await tokenStore.ClearBackupAsync(cancellationToken);
        }
        else
        {
            await tokenStore.ClearAsync(cancellationToken);
        }

        gymContext.Clear();
        navigation.NavigateTo("/admin", forceLoad: true);
    }
}
