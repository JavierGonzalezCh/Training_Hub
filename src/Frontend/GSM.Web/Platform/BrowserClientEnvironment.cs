using System.Reflection;
using GSM.UI.Shared.Diagnostics;
using Microsoft.JSInterop;

namespace GSM.Web.Platform;

/// <summary>El navegador como origen de los reportes de error: <c>/app</c> es la app del miembro, lo demás el panel.</summary>
public sealed class BrowserClientEnvironment(IJSRuntime js) : IClientEnvironment
{
    public string? AppVersion { get; } = typeof(BrowserClientEnvironment).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0];

    public string? ResolvePlatform(string route)
        => route.StartsWith("/app", StringComparison.OrdinalIgnoreCase)
           && (route.Length == 4 || route[4] is '/' or '?' or '#')
            ? ClientPlatformNames.WebMember
            : ClientPlatformNames.WebPanel;

    public async Task<string?> GetDeviceInfoAsync()
    {
        try
        {
            return await js.InvokeAsync<string>("gsmClientInfo.userAgent");
        }
        catch (JSException)
        {
            return null;
        }
    }
}
