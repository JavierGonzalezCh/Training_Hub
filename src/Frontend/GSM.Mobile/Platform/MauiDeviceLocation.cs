using GSM.Member.UI;
using Microsoft.Maui.Devices.Sensors;

namespace GSM.Mobile.Platform;

/// <summary>
/// Ubicación del teléfono (A6). Sin verificar en dispositivo: pide el permiso "mientras se usa" en tiempo de
/// ejecución; si el miembro lo niega o el GPS falla devuelve <c>null</c>.
/// </summary>
public sealed class MauiDeviceLocation : IDeviceLocation
{
    public bool IsSupported => true;

    public async Task<DeviceLocation?> GetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var status = await Microsoft.Maui.ApplicationModel.Permissions
                .RequestAsync<Microsoft.Maui.ApplicationModel.Permissions.LocationWhenInUse>();
            if (status != Microsoft.Maui.ApplicationModel.PermissionStatus.Granted)
            {
                return null;
            }

            var location = await Geolocation.Default.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(10)), cancellationToken);
            return location is null ? null : new DeviceLocation(location.Latitude, location.Longitude);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
