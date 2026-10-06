namespace GSM.Member.UI;

/// <summary>
/// Lector de QR dentro de la app. En la web no hace falta (la cámara del teléfono abre el enlace del
/// QR); la app nativa (MAUI) lo implementa con la cámara del dispositivo.
/// </summary>
public interface IQrScanner
{
    bool IsSupported { get; }

    /// <summary>Texto del primer código leído, o <c>null</c> si el usuario cancela.</summary>
    Task<string?> ScanAsync(CancellationToken cancellationToken = default);
}

public sealed class UnsupportedQrScanner : IQrScanner
{
    public bool IsSupported => false;

    public Task<string?> ScanAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
}

/// <summary>Saca el código del gym de lo que lee el escáner: el enlace del kiosco o el código suelto.</summary>
public static class GymCodeLink
{
    public const string CheckinPath = "/app/checkin";

    public static string? ExtractCode(string? scanned)
    {
        if (string.IsNullOrWhiteSpace(scanned))
        {
            return null;
        }

        scanned = scanned.Trim();

        if (Uri.TryCreate(scanned, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                if (parts[0] == "t" && parts.Length == 2 && parts[1].Length > 0)
                {
                    return Uri.UnescapeDataString(parts[1]);
                }
            }

            return null;
        }

        return scanned.Any(char.IsWhiteSpace) ? null : scanned;
    }

    public static string PathFor(string code) => $"{CheckinPath}?t={Uri.EscapeDataString(code)}";
}
