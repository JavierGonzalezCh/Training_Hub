using GSM.Client.Generated;
using MudBlazor;

namespace GSM.UI.Shared.Theming;

/// <summary>
/// Tema MudBlazor de GSM: Forja, la identidad visual v4.0 (oscuro industrial, rojo). El color de marca
/// del gym (<see cref="MeGym.PrimaryColor"/>) solo lo sustituye cuando el gym eligió uno <b>distinto</b>
/// del rojo por defecto. Los colores de CSS salen de los tokens de <c>gsm.css</c>; los de aquí deben coincidir.
/// </summary>
public static class GsmTheme
{
    // Forja (identidad v4.0). Se conservan como constantes públicas porque hay código que las usa.
    public const string Base = "#161616";
    public const string Panel = "#1F1F1F";
    public const string Border = "#2F2F2F";
    public const string Muted = "#5A5753";
    public const string Silver = "#9A948F";
    public const string Cloud = "#EEEBE6";
    public const string Red = "#B50016";
    public const string RedHot = "#FF3347";
    /// <summary>Verde de estado. Solo significa "activo/correcto", nunca decora.</summary>
    public const string Green = "#3FB950";

    /// <summary>Rojo por defecto con el que nace todo gym (<c>Gym.DefaultPrimaryColor</c>).</summary>
    private const string FactoryPrimary = "#B50016";

    private static readonly string[] Barlow = ["Barlow", "Helvetica Neue", "Arial", "sans-serif"];
    private static readonly string[] JetBrains = ["JetBrains Mono", "Consolas", "monospace"];

    public static MudTheme Build(MeGym? gym = null)
    {
        var gymPrimary = Sanitize(gym?.PrimaryColor);
        if (string.Equals(gymPrimary, FactoryPrimary, StringComparison.OrdinalIgnoreCase))
        {
            gymPrimary = null;
        }

        // El color secundario del gym pinta Tertiary, no Secondary: Secondary es el papel
        // que MudBlazor usa para el texto atenuado (Color.Secondary sale en 86 sitios), así
        // que un color de marca ahí convierte toda la letra pequeña de la app en un acento.
        var gymAccent = Sanitize(gym?.SecondaryColor);

        return Forja(gymPrimary, gymAccent);
    }

    private static MudTheme Forja(string? gymPrimary, string? gymAccent) => new()
    {
        PaletteDark = new PaletteDark
        {
            Primary = gymPrimary ?? Red,
            PrimaryContrastText = "#FFFFFF",
            // Roles separados: cada uno dice una cosa y solo una.
            Secondary = Silver,
            Tertiary = gymAccent ?? Cloud,
            Info = Cloud,
            Success = Green,
            Warning = "#E0A106",
            Error = RedHot,
            // Texto sobre los colores de estado rellenos (chips, botones): oscuro, no el blanco por defecto. Sobre
            // crema, verde, ámbar y el rojo claro el blanco quedaba entre 1,2 y 3,4:1 ("Vigente" 2,54, "Por vencer"
            // 2,27); el negro de la base da de 6 a 17:1. Primary sigue en blanco: sobre #B50016 da 7:1.
            InfoContrastText = Base,
            SuccessContrastText = Base,
            WarningContrastText = Base,
            ErrorContrastText = Base,
            Dark = Base,
            Black = Base,
            Background = Base,
            BackgroundGray = "#1A1A1A",
            Surface = Panel,
            AppbarBackground = Base,
            AppbarText = Cloud,
            DrawerBackground = Base,
            DrawerText = Silver,
            DrawerIcon = Silver,
            TextPrimary = Cloud,
            TextSecondary = Silver,
            TextDisabled = Muted,
            ActionDefault = Silver,
            ActionDisabled = Muted,
            Divider = Border,
            DividerLight = Border,
            LinesDefault = Border,
            LinesInputs = "#3E3E3E",
            TableLines = Border,
            TableStriped = "#222222",
            TableHover = "#262626",
            OverlayDark = "rgba(22, 22, 22, 0.85)",
        },
        Typography = Typography(Barlow, JetBrains),
        LayoutProperties = Layout("2px"),
    };

    private static Typography Typography(string[] sans, string[] mono) => new()
    {
        Default = new DefaultTypography
        {
            FontFamily = sans,
            // Los mismos pasos que --gsm-fs-* en gsm.css: 16 de cuerpo, 14 secundario, 12 de rótulo.
            FontSize = "1rem",
            LineHeight = "1.6",
            FontWeight = "400",
        },
        Body1 = new Body1Typography { FontFamily = sans, FontSize = "1rem", LineHeight = "1.6", LetterSpacing = "0" },
        Body2 = new Body2Typography { FontFamily = sans, FontSize = "0.875rem", LineHeight = "1.5", LetterSpacing = "0" },
        // h6 es el título de sección dentro de una página (bajo el h1 de PageHeader).
        H6 = new H6Typography { FontFamily = sans, FontSize = "1.125rem", FontWeight = "600", LineHeight = "1.35", LetterSpacing = "0" },
        Button = new ButtonTypography { FontFamily = sans, FontWeight = "600", TextTransform = "none" },
        Caption = new CaptionTypography { FontFamily = mono, FontSize = "0.75rem", LetterSpacing = "0.04em" },
        Overline = new OverlineTypography { FontFamily = mono, FontSize = "0.75rem", LineHeight = "1.5", LetterSpacing = "0.12em", TextTransform = "uppercase" },
    };

    private static LayoutProperties Layout(string radius) => new()
    {
        DefaultBorderRadius = radius,
        DrawerWidthLeft = "252px",
        AppbarHeight = "64px",
    };

    private static string? Sanitize(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return null;
        }

        hex = hex.Trim();
        return System.Text.RegularExpressions.Regex.IsMatch(hex, "^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")
            ? hex
            : null;
    }
}
