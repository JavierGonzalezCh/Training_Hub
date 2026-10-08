namespace GSM.Mobile.Platform;

/// <summary>Portapapeles del dispositivo (<c>Clipboard.Default</c> de MAUI).</summary>
public sealed class MauiClipboard : GSM.UI.Shared.IClipboard
{
    public async Task<bool> CopyAsync(string text, CancellationToken cancellationToken = default)
    {
        try
        {
            await Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default.SetTextAsync(text);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
