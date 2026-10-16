namespace GSM.Member.UI;

/// <summary>Posición del teléfono del miembro. Solo viaja con la petición de check-in; nunca se guarda.</summary>
public sealed record DeviceLocation(double Latitude, double Longitude);

/// <summary>
/// Ubicación del dispositivo para el check-in con el QR fijo (A6). La web usa la API de geolocalización del
/// navegador; la app nativa (MAUI) usa el sensor del teléfono. Devuelve <c>null</c> si el miembro niega el permiso,
/// el GPS falla o se agota el tiempo: el servidor lo trata como "no compartió" y la entrada no suma a la racha.
/// </summary>
public interface IDeviceLocation
{
    bool IsSupported { get; }

    Task<DeviceLocation?> GetAsync(CancellationToken cancellationToken = default);
}

public sealed class UnsupportedDeviceLocation : IDeviceLocation
{
    public bool IsSupported => false;

    public Task<DeviceLocation?> GetAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<DeviceLocation?>(null);
}
