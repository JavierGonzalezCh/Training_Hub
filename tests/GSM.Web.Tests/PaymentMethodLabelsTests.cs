using GSM.UI.Shared.Formatting;

namespace GSM.Web.Tests;

public class PaymentMethodLabelsTests
{
    [Theory]
    [InlineData("Cash", "Efectivo")]
    [InlineData("Transfer", "Transferencia")]
    [InlineData("Card", "Tarjeta")]
    [InlineData("Other", "Otro")]
    public void Translates_known_methods(string method, string expected)
        => Assert.Equal(expected, PaymentMethodLabels.Label(method));

    [Fact]
    public void All_lists_the_four_methods_in_backend_order()
        => Assert.Equal(
            [(0, "Efectivo"), (1, "Transferencia"), (2, "Tarjeta"), (3, "Otro")],
            PaymentMethodLabels.All);
}
