using GSM.Member.UI;
using Microsoft.JSInterop;

namespace GSM.Web.Platform;

/// <summary>Ubicación del navegador (<c>navigator.geolocation</c>, vía <c>gsm-geolocation.js</c>).</summary>
public sealed class BrowserDeviceLocation(IJSRuntime js) : IDeviceLocation
{
    public bool IsSupported => true;

    public async Task<DeviceLocation?> GetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await js.InvokeAsync<DeviceLocation?>("gsmGeolocation.get", cancellationToken, 10000);
        }
        catch (Exception ex) when (ex is JSException or TaskCanceledException or InvalidOperationException)
        {
            return null;
        }
    }
}
