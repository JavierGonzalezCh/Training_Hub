namespace GSM.UI.Shared.Security;

/// <summary>
/// Biometría del dispositivo para el bloqueo del modo staff (apps y vistas §7). La implementa solo la app
/// nativa; en el navegador no hay bloqueo (<see cref="NullDeviceLock"/>).
/// </summary>
public interface IDeviceLock
{
    /// <summary>Si este host ofrece bloqueo de pantalla. En la web es <c>false</c> y el panel funciona como siempre.</summary>
    bool IsSupported { get; }

    /// <summary>
    /// Si el teléfono tiene huella, rostro o PIN/patrón del sistema configurado. Si es <c>false</c>, la app
    /// pide crear un PIN propio.
    /// </summary>
    Task<bool> IsDeviceAuthAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>Pide huella, rostro o el PIN del teléfono. <c>true</c> solo si la persona se autenticó.</summary>
    Task<bool> AuthenticateAsync(string reason, CancellationToken cancellationToken = default);
}

/// <summary>Sin bloqueo: el host web.</summary>
public sealed class NullDeviceLock : IDeviceLock
{
    public bool IsSupported => false;

    public Task<bool> IsDeviceAuthAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task<bool> AuthenticateAsync(string reason, CancellationToken cancellationToken = default) => Task.FromResult(false);
}

/// <summary>Ciclo de vida de la app: cuándo pasa a segundo plano y cuándo vuelve.</summary>
public interface IAppLifecycle
{
    event Action? Backgrounded;

    event Action? Foregrounded;

    /// <summary>Engancha los eventos de la ventana (puede no existir cuando se construye el contenedor). Idempotente.</summary>
    void Attach();
}

public sealed class NullAppLifecycle : IAppLifecycle
{
    public event Action? Backgrounded { add { } remove { } }

    public event Action? Foregrounded { add { } remove { } }

    public void Attach()
    {
    }
}

/// <summary>Almacén seguro de pares clave/valor (SecureStorage en MAUI).</summary>
public interface ISecureValueStore
{
    Task<string?> GetAsync(string key);

    Task SetAsync(string key, string value);

    void Remove(string key);
}
