using GSM.Client.Generated;
using GSM.UI.Shared.Theming;
using Xunit;

namespace GSM.Web.Tests;

/// <summary>O5 — el color secundario del gym llega al CSS como <c>--gsm-brand-2</c>, con o sin color principal válido.</summary>
public sealed class GymBrandSecondaryTests
{
    [Fact]
    public void The_secondary_color_is_emitted_next_to_the_primary_one()
    {
        var css = GymBrand.Css(new MeGym { PrimaryColor = "#B50016", SecondaryColor = "#00AAFF" });

        Assert.Contains("--gsm-accent:#B50016", css);
        Assert.Contains("--gsm-brand-2:#00AAFF", css);
    }

    [Fact]
    public void The_secondary_color_travels_even_without_a_valid_primary_one()
    {
        var css = GymBrand.Css(new MeGym { PrimaryColor = "no-es-un-color", SecondaryColor = "#00AAFF" });

        Assert.Contains("--gsm-brand-2:#00AAFF", css);
        Assert.DoesNotContain("--gsm-accent:", css);
    }

    [Fact]
    public void Without_any_valid_color_there_is_no_css()
        => Assert.Equal(string.Empty, GymBrand.Css(new MeGym { PrimaryColor = "x", SecondaryColor = "y" }));

    [Fact]
    public void The_theme_uses_the_carbon_background_and_a_lighter_card_surface()
    {
        Assert.Equal("#161616", GsmTheme.Base);
        Assert.Equal("#1F1F1F", GsmTheme.Panel);
    }
}
