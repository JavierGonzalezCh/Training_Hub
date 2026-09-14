using GSM.UI.Shared.Formatting;
using MudBlazor;

namespace GSM.Web.Tests;

public class StatusLabelsTests
{
    [Theory]
    [InlineData("Active", "Activo", Color.Info)]
    [InlineData("Inactive", "Inactivo", Color.Default)]
    [InlineData("Expired", "Vencido", Color.Error)]
    [InlineData("Pending", "Pendiente", Color.Default)]
    public void Maps_known_statuses_to_label_and_brand_color(string status, string label, Color color)
    {
        Assert.Equal(label, StatusLabels.Label(status));
        Assert.Equal(color, StatusLabels.Color(status));
    }

    [Fact]
    public void Falls_back_to_the_raw_value_for_an_unknown_status()
        => Assert.Equal("Weird", StatusLabels.Label("Weird"));
}
