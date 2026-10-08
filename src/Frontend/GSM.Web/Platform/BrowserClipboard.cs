using GSM.UI.Shared;
using Microsoft.JSInterop;

namespace GSM.Web.Platform;

/// <summary>Portapapeles del navegador (<c>navigator.clipboard</c>).</summary>
public sealed class BrowserClipboard(IJSRuntime js) : IClipboard
{
    public async Task<bool> CopyAsync(string text, CancellationToken cancellationToken = default)
    {
        try
        {
            await js.InvokeVoidAsync("navigator.clipboard.writeText", cancellationToken, text);
            return true;
        }
        catch (JSException)
        {
            return false;
        }
    }
}
