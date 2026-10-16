using GSM.Client.Generated;

namespace GSM.UI.Shared.Push;

/// <summary>Estado del permiso de notificaciones del dispositivo.</summary>
public enum PushPermissionState
{
    /// <summary>El navegador o el host no soporta push (o no hay implementación, como en la app nativa por ahora).</summary>
    Unsupported,

    /// <summary>iPhone con la web sin instalar: solo recibe push desde la pantalla de inicio (iOS 16.4 o superior).</summary>
    NeedsInstall,

    /// <summary>Todavía no se pidió el permiso.</summary>
    Default,

    Granted,

    /// <summary>La persona lo negó: solo puede cambiarlo desde los ajustes del navegador.</summary>
    Denied,
}

/// <summary>
/// Notificaciones push del dispositivo. En la web las implementa el navegador (permiso, service worker y
/// Firebase Cloud Messaging por JS interop); la app nativa tendrá la suya en N3 (mismo patrón que
/// <see cref="IClipboard"/>). Sin implementación registrada vale <see cref="UnsupportedPushNotifications"/>.
/// </summary>
public interface IPushNotifications
{
    Task<PushPermissionState> GetStateAsync(CancellationToken cancellationToken = default);

    /// <summary>¿Está la app instalada en el dispositivo (modo independiente)? El panel pide permiso solo así.</summary>
    Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Pide el permiso al navegador (si aún no lo dio) y devuelve el token de Firebase del dispositivo;
    /// <c>null</c> si la persona lo negó o no se pudo obtener. Con el permiso ya concedido no pregunta de nuevo.
    /// </summary>
    Task<string?> RequestTokenAsync(PushConfigResponse config, CancellationToken cancellationToken = default);

    /// <summary>El token actual sin pedir permiso (<c>null</c> si no hay permiso o no se puede obtener).</summary>
    Task<string?> GetTokenAsync(PushConfigResponse config, CancellationToken cancellationToken = default);

    /// <summary>Borra el token del dispositivo (al cerrar sesión).</summary>
    Task ForgetTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>¿La persona descartó la invitación a activar los avisos? Se recuerda en el dispositivo.</summary>
    Task<bool> IsPromptDismissedAsync(CancellationToken cancellationToken = default);

    Task DismissPromptAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Plataforma con la que este host registra el dispositivo en la API (<see cref="PushPlatforms"/>). La web
    /// la deja en <c>null</c> y cada pantalla pasa la suya (<c>web_member</c> / <c>web_panel</c>); la app nativa
    /// devuelve <c>android</c> sin importar qué pantalla lo pida, porque el token es de FCM nativo (N3).
    /// </summary>
    string? PlatformOverride => null;
}

/// <summary>Host sin push: nada que ofrecer.</summary>
public sealed class UnsupportedPushNotifications : IPushNotifications
{
    public Task<PushPermissionState> GetStateAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(PushPermissionState.Unsupported);

    public Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task<string?> RequestTokenAsync(PushConfigResponse config, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    public Task<string?> GetTokenAsync(PushConfigResponse config, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    public Task ForgetTokenAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<bool> IsPromptDismissedAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task DismissPromptAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
