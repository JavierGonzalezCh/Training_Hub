using GSM.Client.Generated;
using MudBlazor;

namespace GSM.UI.Shared.Theming;

/// <summary>
/// Tema MudBlazor de GSM: identidad visual v4.0, <b>oscuro por defecto</b> (negro casi puro como
/// lienzo; rojo = marca/acción/urgencia, cian = dato/positivo/navegación).
/// El branding por gym (<see cref="MeGym.PrimaryColor"/> / <see cref="MeGym.SecondaryColor"/>)
/// sobrescribe el rojo y el cian de marca cuando el gym define los suyos.
/// La tipografía (Anton / Inter / JetBrains Mono) se aplica en <c>gsm.css</c>.
/// </summary>
public static class GsmTheme
{
    public const string Base = "#0F0E0D";
    public const string Panel = "#191817";
    public const string Border = "#282624";
    public const string Muted = "#4A4844";
    public const string Silver = "#8A8480";
    public const string Cloud = "#EEEBE6";
    public const string Red = "#B50016";
    public const string RedHot = "#E8001E";
    public const string Cyan = "#0AAFCA";

    private static readonly string[] Sans = ["Inter", "Helvetica Neue", "Helvetica", "Arial", "sans-serif"];
    private static readonly string[] Mono = ["JetBrains Mono", "SF Mono", "Cascadia Code", "Consolas", "monospace"];

    public static MudTheme Build(MeGym? gym = null)
    {
        var primary = Sanitize(gym?.PrimaryColor) ?? Red;
        var secondary = Sanitize(gym?.SecondaryColor) ?? Cyan;

        return new MudTheme
        {
            PaletteDark = new PaletteDark
            {
                Primary = primary,
                Secondary = secondary,
                Tertiary = secondary,
                Info = secondary,
                Success = secondary,
                Warning = "#E0A106",
                Error = RedHot,
                Dark = Base,

                Black = Base,
                Background = Base,
                BackgroundGray = "#141312",
                Surface = Panel,
                AppbarBackground = Panel,
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
                LinesInputs = Border,
                TableLines = Border,
                TableStriped = "#1E1D1B",
                TableHover = "#211F1D",
                OverlayDark = "rgba(15, 14, 13, 0.85)",
            },
            Typography = new Typography
            {
                Default = new DefaultTypography
                {
                    FontFamily = Sans,
                    FontSize = "0.9375rem",
                    LineHeight = "1.7",
                    FontWeight = "400",
                },
                Button = new ButtonTypography { FontFamily = Sans, FontWeight = "600", TextTransform = "none" },
                Caption = new CaptionTypography { FontFamily = Mono, FontSize = "0.75rem", LetterSpacing = "0.06em" },
                Overline = new OverlineTypography { FontFamily = Mono, LetterSpacing = "0.12em", TextTransform = "uppercase" },
            },
            LayoutProperties = new LayoutProperties
            {
                DefaultBorderRadius = "6px",
                DrawerWidthLeft = "244px",
                AppbarHeight = "60px",
            },
        };
    }

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
