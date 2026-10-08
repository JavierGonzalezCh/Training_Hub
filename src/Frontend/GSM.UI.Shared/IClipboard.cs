namespace GSM.UI.Shared;

/// <summary>
/// Copiar texto al portapapeles. En la web lo implementa el navegador (JS interop); la app nativa
/// (MAUI) usa el portapapeles del dispositivo. Mismo patrón que <c>IQrScanner</c>.
/// </summary>
public interface IClipboard
{
    /// <summary>Copia el texto; <c>false</c> si el sistema no lo permitió.</summary>
    Task<bool> CopyAsync(string text, CancellationToken cancellationToken = default);
}
