using Android.Gms.Extensions;
using Firebase.Messaging;
using GSM.Client.Generated;
using GSM.UI.Shared.Push;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace GSM.Mobile.Platform;

/// <summary>
/// Push de la app Android (N3): permiso de notificaciones (Android 13+), token de Firebase Cloud Messaging
/// nativo y baja del token al cerrar sesión. Implementa el mismo <see cref="IPushNotifications"/> que la web
/// (<c>BrowserPushNotifications</c>), así que <see cref="PushRegistrationService"/> y la invitación a activar los
/// avisos funcionan igual; el dispositivo se registra con plataforma <c>android</c>.
/// <para>
/// <b>Sin verificar en dispositivo ni con Firebase real.</b> Si Firebase no está configurado en la API, los
/// métodos de token escriben el motivo en logcat (etiqueta <c>GSM.Push</c>) y devuelven <c>null</c>: la app
/// sigue funcionando sin push.
/// </para>
/// </summary>
public sealed class AndroidPushNotifications : IPushNotifications
{
    private const string KeyAsked = "gsm.push.permission_asked";
    private const string KeyDismissed = "gsm.push.prompt_dismissed";
    private const string Tag = "GSM.Push";

    public string? PlatformOverride => PushPlatforms.Android;

    public async Task<PushPermissionState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var status = await MainThread.InvokeOnMainThreadAsync(() => Permissions.CheckStatusAsync<Permissions.PostNotifications>());
            return status switch
            {
                PermissionStatus.Granted or PermissionStatus.Limited => PushPermissionState.Granted,
                // Android no distingue «nunca preguntado» de «negado»: se recuerda si ya se pidió.
                PermissionStatus.Denied or PermissionStatus.Disabled when Preferences.Default.Get(KeyAsked, false) => PushPermissionState.Denied,
                PermissionStatus.Restricted => PushPermissionState.Denied,
                _ => PushPermissionState.Default,
            };
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn(Tag, $"No se pudo leer el permiso de notificaciones: {ex.Message}");
            return PushPermissionState.Unsupported;
        }
    }

    // La app nativa ya está «instalada»: el panel puede ofrecer los avisos sin exigir modo independiente.
    public Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public async Task<string?> RequestTokenAsync(PushConfigResponse config, CancellationToken cancellationToken = default)
    {
        try
        {
            var status = await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                Preferences.Default.Set(KeyAsked, true);
                return await Permissions.RequestAsync<Permissions.PostNotifications>();
            });

            return status is PermissionStatus.Granted or PermissionStatus.Limited
                ? await FetchTokenAsync(config)
                : null;
        }
        catch (Exception ex)
        {
            Android.Util.Log.Error(Tag, $"No se pudo activar el push: {ex.Message}");
            return null;
        }
    }

    public async Task<string?> GetTokenAsync(PushConfigResponse config, CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetStateAsync(cancellationToken) == PushPermissionState.Granted
                ? await FetchTokenAsync(config)
                : null;
        }
        catch (Exception ex)
        {
            Android.Util.Log.Error(Tag, $"No se pudo leer el token de Firebase: {ex.Message}");
            return null;
        }
    }

    public async Task ForgetTokenAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var context = Android.App.Application.Context;
            if (AndroidFirebase.IsInitialized(context))
            {
                await FirebaseMessaging.Instance.DeleteToken();
            }
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn(Tag, $"No se pudo borrar el token de Firebase: {ex.Message}");
        }
    }

    public Task<bool> IsPromptDismissedAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Preferences.Default.Get(KeyDismissed, false));

    public Task DismissPromptAsync(CancellationToken cancellationToken = default)
    {
        Preferences.Default.Set(KeyDismissed, true);
        return Task.CompletedTask;
    }

    private static async Task<string?> FetchTokenAsync(PushConfigResponse config)
    {
        var context = Android.App.Application.Context;
        AndroidFirebase.EnsureInitialized(context, config.ProjectId, config.ApiKey, config.AppId, config.MessagingSenderId);
        var token = await FirebaseMessaging.Instance.GetToken().AsAsync<Java.Lang.String>();
        return token?.ToString();
    }
}
