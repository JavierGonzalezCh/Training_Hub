using GSM.UI.Shared.Security;
#if ANDROID
using Plugin.Fingerprint;
using Plugin.Fingerprint.Abstractions;
#endif

namespace GSM.Mobile.Platform;

/// <summary>Registro del bloqueo del modo staff (AP5). Una sola línea en <c>MauiProgram</c>.</summary>
public static class DeviceLockRegistration
{
    public static IServiceCollection AddGsmDeviceLock(this IServiceCollection services)
    {
        services.AddSingleton<ISecureValueStore, MauiSecureValueStore>();
        services.AddSingleton<IDeviceLock, MauiDeviceLock>();
        services.AddSingleton<IAppLifecycle, MauiAppLifecycle>();
        return services;
    }
}

/// <summary>SecureStorage de MAUI como almacén del hash del PIN.</summary>
public sealed class MauiSecureValueStore : ISecureValueStore
{
    public async Task<string?> GetAsync(string key)
    {
        try
        {
            return await SecureStorage.Default.GetAsync(key);
        }
        catch (Exception)
        {
            return null; // almacén corrupto o clave perdida: se trata como "sin PIN"
        }
    }

    public Task SetAsync(string key, string value) => SecureStorage.Default.SetAsync(key, value);

    public void Remove(string key) => SecureStorage.Default.Remove(key);
}

/// <summary>
/// Huella, rostro o PIN del teléfono. En Android usa Plugin.Fingerprint con autenticación alternativa
/// (credencial del dispositivo). En Windows no hay biometría: el bloqueo cae al PIN propio.
/// </summary>
public sealed class MauiDeviceLock : IDeviceLock
{
    public bool IsSupported => true;

#if ANDROID
    public MauiDeviceLock()
    {
        CrossFingerprint.SetCurrentActivityResolver(() => Microsoft.Maui.ApplicationModel.Platform.CurrentActivity);
    }

    public async Task<bool> IsDeviceAuthAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await CrossFingerprint.Current.IsAvailableAsync(allowAlternativeAuthentication: true);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<bool> AuthenticateAsync(string reason, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new AuthenticationRequestConfiguration("GSM", reason)
            {
                AllowAlternativeAuthentication = true,
            };
            var result = await CrossFingerprint.Current.AuthenticateAsync(request, cancellationToken);
            return result.Authenticated;
        }
        catch (Exception)
        {
            return false;
        }
    }
#else
    public Task<bool> IsDeviceAuthAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task<bool> AuthenticateAsync(string reason, CancellationToken cancellationToken = default) => Task.FromResult(false);
#endif
}

/// <summary>
/// Ciclo de vida desde los eventos de la ventana de MAUI. Se usa <c>Stopped</c>/<c>Resumed</c> y no
/// <c>Deactivated</c>/<c>Activated</c>: el aviso de huella del sistema desactiva la ventana y se bloquearía solo.
/// </summary>
public sealed class MauiAppLifecycle : IAppLifecycle
{
    private Window? _window;

    public event Action? Backgrounded;

    public event Action? Foregrounded;

    public void Attach()
    {
        var window = Application.Current?.Windows.FirstOrDefault();
        if (window is null || ReferenceEquals(window, _window))
        {
            return;
        }

        if (_window is not null)
        {
            _window.Stopped -= OnStopped;
            _window.Resumed -= OnResumed;
        }

        _window = window;
        window.Stopped += OnStopped;
        window.Resumed += OnResumed;
    }

    private void OnStopped(object? sender, EventArgs e) => Backgrounded?.Invoke();

    private void OnResumed(object? sender, EventArgs e) => Foregrounded?.Invoke();
}
