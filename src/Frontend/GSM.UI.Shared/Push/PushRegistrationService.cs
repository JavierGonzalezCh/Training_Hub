using GSM.Client.Errors;
using GSM.Client.Generated;

namespace GSM.UI.Shared.Push;

/// <summary>Plataformas que registra la web (los mismos valores que acepta la API).</summary>
public static class PushPlatforms
{
    public const string WebMember = "web_member";
    public const string WebPanel = "web_panel";
}

public enum PushEnableResult
{
    Enabled,

    /// <summary>El servidor no tiene Firebase configurado.</summary>
    Unavailable,

    /// <summary>La persona negó el permiso (o el dispositivo no pudo dar un token).</summary>
    Denied,

    Failed,
}

/// <summary>
/// Coordina permiso, token de Firebase y registro del dispositivo en la API (spec Notifications §6):
/// se ofrece en el momento en que tiene sentido (nunca al abrir la app), se registra al iniciar sesión si ya
/// hay permiso y se da de baja al cerrarla.
/// </summary>
public sealed class PushRegistrationService(IPushNotifications push, IPushClient client)
{
    private PushConfigResponse? _config;

    /// <summary>
    /// ¿Conviene ofrecer activar los avisos ahora? Solo si el servidor tiene Firebase, el dispositivo lo soporta y
    /// la persona no lo activó ni descartó la invitación. En iPhone sin instalar se ofrece la guía de instalación.
    /// </summary>
    public async Task<PushPermissionState?> GetOfferAsync(CancellationToken cancellationToken = default)
    {
        var state = await push.GetStateAsync(cancellationToken);
        if (state is not (PushPermissionState.Default or PushPermissionState.NeedsInstall))
        {
            return null;
        }

        if (await push.IsPromptDismissedAsync(cancellationToken))
        {
            return null;
        }

        var config = await GetConfigAsync(cancellationToken);
        return config is { Enabled: true } ? state : null;
    }

    public async Task<PushEnableResult> EnableAsync(string platform, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = await GetConfigAsync(cancellationToken);
            if (config is not { Enabled: true })
            {
                return PushEnableResult.Unavailable;
            }

            var token = await push.RequestTokenAsync(config, cancellationToken);
            if (string.IsNullOrWhiteSpace(token))
            {
                return PushEnableResult.Denied;
            }

            await client.RegisterPushDeviceAsync(
                new RegisterPushDeviceRequest { Token = token, Platform = platform }, cancellationToken);
            return PushEnableResult.Enabled;
        }
        catch (GsmApiException)
        {
            return PushEnableResult.Failed;
        }
    }

    /// <summary>
    /// Al iniciar sesión: si la persona ya dio el permiso, registra el token vigente (puede haber rotado).
    /// Silencioso: nunca pregunta ni falla.
    /// </summary>
    public async Task SyncAsync(string platform, CancellationToken cancellationToken = default)
    {
        try
        {
            if (await push.GetStateAsync(cancellationToken) != PushPermissionState.Granted)
            {
                return;
            }

            var config = await GetConfigAsync(cancellationToken);
            if (config is not { Enabled: true })
            {
                return;
            }

            var token = await push.GetTokenAsync(config, cancellationToken);
            if (!string.IsNullOrWhiteSpace(token))
            {
                await client.RegisterPushDeviceAsync(
                    new RegisterPushDeviceRequest { Token = token, Platform = platform }, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is GsmApiException or InvalidOperationException)
        {
            // Registrar es best-effort: la próxima sesión lo reintenta.
        }
    }

    /// <summary>Al cerrar sesión: da de baja el dispositivo y olvida el token. Best-effort.</summary>
    public async Task DisableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var config = _config;
            if (config is { Enabled: true } && await push.GetStateAsync(cancellationToken) == PushPermissionState.Granted)
            {
                var token = await push.GetTokenAsync(config, cancellationToken);
                if (!string.IsNullOrWhiteSpace(token))
                {
                    await client.UnregisterPushDeviceAsync(
                        new UnregisterPushDeviceRequest { Token = token }, cancellationToken);
                }
            }

            await push.ForgetTokenAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is GsmApiException or InvalidOperationException)
        {
            // El cierre de sesión no debe fallar por esto.
        }
    }

    public Task DismissAsync(CancellationToken cancellationToken = default) => push.DismissPromptAsync(cancellationToken);

    private async Task<PushConfigResponse?> GetConfigAsync(CancellationToken cancellationToken)
    {
        if (_config is not null)
        {
            return _config;
        }

        try
        {
            _config = await client.GetPushConfigAsync(cancellationToken);
        }
        catch (GsmApiException)
        {
            return null;
        }

        return _config;
    }
}
