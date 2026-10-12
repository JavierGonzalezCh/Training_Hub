using GSM.Client.Generated;
using GSM.UI.Shared.Push;
using Microsoft.JSInterop;

namespace GSM.Web.Platform;

/// <summary>
/// Push del navegador: permiso de notificaciones, service worker y token de Firebase Cloud Messaging por JS
/// interop (<c>wwwroot/gsm-push.js</c>). Cualquier fallo de JS se traduce en «no soportado» o «sin token»: el
/// push nunca debe romper la app.
/// <para>
/// <b>Sin verificar con Firebase real:</b> todavía no hay proyecto de Firebase.
/// </para>
/// </summary>
public sealed class BrowserPushNotifications(IJSRuntime js) : IPushNotifications
{
    public async Task<PushPermissionState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await js.InvokeAsync<string>("gsmPush.state", cancellationToken) switch
            {
                "needs-install" => PushPermissionState.NeedsInstall,
                "default" => PushPermissionState.Default,
                "granted" => PushPermissionState.Granted,
                "denied" => PushPermissionState.Denied,
                _ => PushPermissionState.Unsupported,
            };
        }
        catch (JSException)
        {
            return PushPermissionState.Unsupported;
        }
    }

    public async Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default)
        => await TryAsync(() => js.InvokeAsync<bool>("gsmPush.isInstalled", cancellationToken), false);

    public async Task<string?> RequestTokenAsync(PushConfigResponse config, CancellationToken cancellationToken = default)
        => await TryAsync<string?>(() => js.InvokeAsync<string?>("gsmPush.requestToken", cancellationToken, ToJs(config)), null);

    public async Task<string?> GetTokenAsync(PushConfigResponse config, CancellationToken cancellationToken = default)
        => await TryAsync<string?>(() => js.InvokeAsync<string?>("gsmPush.getToken", cancellationToken, ToJs(config)), null);

    public async Task ForgetTokenAsync(CancellationToken cancellationToken = default)
        => await TryAsync(async () =>
        {
            await js.InvokeVoidAsync("gsmPush.forgetToken", cancellationToken);
            return true;
        }, false);

    public async Task<bool> IsPromptDismissedAsync(CancellationToken cancellationToken = default)
        => await TryAsync(() => js.InvokeAsync<bool>("gsmPush.isDismissed", cancellationToken), false);

    public async Task DismissPromptAsync(CancellationToken cancellationToken = default)
        => await TryAsync(async () =>
        {
            await js.InvokeVoidAsync("gsmPush.dismiss", cancellationToken);
            return true;
        }, false);

    private static object ToJs(PushConfigResponse config) => new
    {
        projectId = config.ProjectId,
        apiKey = config.ApiKey,
        appId = config.AppId,
        messagingSenderId = config.MessagingSenderId,
        vapidKey = config.VapidKey,
    };

    private static async Task<T> TryAsync<T>(Func<ValueTask<T>> call, T fallback)
    {
        try
        {
            return await call();
        }
        catch (JSException)
        {
            return fallback;
        }
    }
}
