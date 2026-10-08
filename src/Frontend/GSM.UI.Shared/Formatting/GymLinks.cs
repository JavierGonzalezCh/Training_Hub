using System.Globalization;

namespace GSM.UI.Shared.Formatting;

/// <summary>Enlaces externos del gym (mapa y WhatsApp), sin cuentas ni claves de ningún servicio.</summary>
public static class GymLinks
{
    /// <summary>Abre el sitio en Google Maps: por coordenadas si las hay, si no por la dirección. <c>null</c> si no hay nada.</summary>
    public static string? MapUrl(double? latitude, double? longitude, string? address, string? city)
    {
        if (latitude is { } lat && longitude is { } lon)
        {
            return "https://www.google.com/maps/search/?api=1&query="
                   + lat.ToString("0.######", CultureInfo.InvariantCulture)
                   + ","
                   + lon.ToString("0.######", CultureInfo.InvariantCulture);
        }

        var text = string.Join(", ", new[] { address, city }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return text.Length == 0
            ? null
            : "https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString(text);
    }

    /// <summary>Cómo llegar desde donde esté el miembro (Google Maps calcula la ruta).</summary>
    public static string? DirectionsUrl(double? latitude, double? longitude, string? address, string? city)
    {
        var destination = latitude is { } lat && longitude is { } lon
            ? lat.ToString("0.######", CultureInfo.InvariantCulture) + "," + lon.ToString("0.######", CultureInfo.InvariantCulture)
            : string.Join(", ", new[] { address, city }.Where(s => !string.IsNullOrWhiteSpace(s)));

        return destination.Length == 0
            ? null
            : "https://www.google.com/maps/dir/?api=1&destination=" + Uri.EscapeDataString(destination);
    }

    /// <summary>Chat de WhatsApp con el gym (<c>wa.me</c> pide el número sin el signo +).</summary>
    public static string? WhatsAppUrl(string? e164)
    {
        var digits = new string((e164 ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 0 ? null : "https://wa.me/" + digits;
    }

    /// <summary>Llamada telefónica (<c>tel:</c>).</summary>
    public static string? TelUrl(string? phone)
        => string.IsNullOrWhiteSpace(phone) ? null : "tel:" + phone.Trim();
}
