using GSM.UI.Shared.Theming;

namespace GSM.Web.Tests;

/// <summary>
/// Texto sobre los colores de estado rellenos (chips "Vigente", "Por vencer", botones de error): debe llegar a
/// 4,5:1 (WCAG AA). Con el blanco por defecto de MudBlazor quedaba entre 1,2 y 3,4:1.
/// </summary>
public class ThemeContrastTests
{
    private static double Luminance(string hex)
    {
        static double Channel(int v)
        {
            var c = v / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        var h = hex.TrimStart('#');
        return 0.2126 * Channel(Convert.ToInt32(h[..2], 16))
               + 0.7152 * Channel(Convert.ToInt32(h[2..4], 16))
               + 0.0722 * Channel(Convert.ToInt32(h[4..6], 16));
    }

    private static double Ratio(string a, string b)
    {
        var (x, y) = (Luminance(a), Luminance(b));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    private static string Hex(MudBlazor.Utilities.MudColor color) => color.Value[..7];

    [Fact]
    public void Text_on_every_filled_status_color_reaches_AA()
    {
        var palette = GsmTheme.Build().PaletteDark;

        Assert.True(Ratio(Hex(palette.InfoContrastText), Hex(palette.Info)) >= 4.5, "Info");
        Assert.True(Ratio(Hex(palette.SuccessContrastText), Hex(palette.Success)) >= 4.5, "Success");
        Assert.True(Ratio(Hex(palette.WarningContrastText), Hex(palette.Warning)) >= 4.5, "Warning");
        Assert.True(Ratio(Hex(palette.ErrorContrastText), Hex(palette.Error)) >= 4.5, "Error");
        Assert.True(Ratio(Hex(palette.PrimaryContrastText), Hex(palette.Primary)) >= 4.5, "Primary");
    }
}
