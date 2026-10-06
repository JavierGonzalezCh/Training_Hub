using System.Globalization;
using System.Text.RegularExpressions;
using GSM.Client.Generated;

namespace GSM.UI.Shared.Theming;

/// <summary>
/// Marca del gym llevada al CSS: los colores que el dueño eligió en Configuración sustituyen a los tokens
/// de acento de <c>gsm.css</c> (<c>--gsm-accent</c> y derivados), así toda la interfaz —tabs, botones,
/// bordes activos, tarjetas destacadas— sale con los colores del gimnasio y no con los de GSM.
/// </summary>
public static partial class GymBrand
{
    [GeneratedRegex("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")]
    private static partial Regex HexPattern();

    /// <summary>Reglas <c>:root</c> con los tokens de marca del gym, o vacío si no hay un color válido.</summary>
    public static string Css(MeGym? gym)
    {
        if (!TryParse(gym?.PrimaryColor, out var r, out var g, out var b))
        {
            return string.Empty;
        }

        var accent = Hex(r, g, b);

        // El acento como texto sobre fondo oscuro necesita contraste: si es oscuro se aclara hacia blanco.
        var luminance = Luminance(r, g, b);
        var textMix = luminance < 0.30 ? 0.42 : luminance < 0.45 ? 0.20 : 0.0;
        var accentText = Hex(Mix(r, 255, textMix), Mix(g, 255, textMix), Mix(b, 255, textMix));
        var onAccent = luminance > 0.55 ? "#0F0E0D" : "#FFFFFF";

        var css = $"--gsm-accent:{accent};--gsm-accent-text:{accentText};--gsm-on-accent:{onAccent};"
            + $"--gsm-accent-soft:rgba({r},{g},{b},0.16);--gsm-grid-line:rgba({r},{g},{b},0.07);--gsm-ring:{accentText};";

        if (TryParse(gym?.SecondaryColor, out var r2, out var g2, out var b2))
        {
            css += $"--gsm-brand-2:{Hex(r2, g2, b2)};";
        }

        return $":root,:root[data-gsm-theme=\"forja\"]{{{css}}}";
    }

    /// <summary>Inicial del gym para la marca cuando no hay logo.</summary>
    public static string Initial(string? name)
        => string.IsNullOrWhiteSpace(name) ? "G" : name.Trim()[..1].ToUpperInvariant();

    private static bool TryParse(string? hex, out int r, out int g, out int b)
    {
        r = g = b = 0;
        if (string.IsNullOrWhiteSpace(hex) || !HexPattern().IsMatch(hex.Trim()))
        {
            return false;
        }

        var h = hex.Trim().TrimStart('#');
        if (h.Length == 3)
        {
            h = string.Concat(h.Select(c => $"{c}{c}"));
        }

        r = int.Parse(h[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        g = int.Parse(h[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        b = int.Parse(h[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return true;
    }

    private static string Hex(int r, int g, int b) => $"#{r:X2}{g:X2}{b:X2}";

    private static int Mix(int from, int to, double amount) => (int)Math.Round(from + (to - from) * amount);

    // Luminancia relativa (WCAG), 0 = negro, 1 = blanco.
    private static double Luminance(int r, int g, int b)
    {
        static double Channel(int v)
        {
            var c = v / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
    }
}
