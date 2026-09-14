using GSM.UI.Shared.Formatting;

namespace GSM.Web.Tests;

public class SuperAdminLabelsTests
{
    [Theory]
    [InlineData("Trial", "Prueba")]
    [InlineData("Overdue", "Moroso")]
    [InlineData("Disabled", "Deshabilitado")]
    public void Translates_effective_status(string status, string expected)
        => Assert.Equal(expected, SuperAdminLabels.StatusLabel(status));

    [Fact]
    public void Plans_match_the_closed_backend_enum()
        => Assert.Equal([(0, "Basic"), (1, "Pro"), (2, "Enterprise")], SuperAdminLabels.Plans);
}
