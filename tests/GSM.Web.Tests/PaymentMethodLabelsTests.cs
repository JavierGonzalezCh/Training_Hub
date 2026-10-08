using GSM.UI.Shared.Formatting;

namespace GSM.Web.Tests;

public class PaymentMethodLabelsTests
{
    [Theory]
    [InlineData("Cash", "Efectivo")]
    [InlineData("Card", "Tarjeta")]
    [InlineData("Nequi", "Nequi")]
    [InlineData("Daviplata", "Daviplata")]
    [InlineData("Breb", "Bre-B")]
    [InlineData("BankTransfer", "Transferencia bancaria")]
    [InlineData("BankDeposit", "Consignación")]
    [InlineData("Other", "Otro")]
    public void Translates_known_methods(string method, string expected)
        => Assert.Equal(expected, PaymentMethodLabels.Label(method));

    [Fact]
    public void All_lists_the_eight_methods_in_backend_order()
        => Assert.Equal(
            [
                (0, "Efectivo"), (1, "Tarjeta"), (2, "Nequi"), (3, "Daviplata"),
                (4, "Bre-B"), (5, "Transferencia bancaria"), (6, "Consignación"), (7, "Otro"),
            ],
            PaymentMethodLabels.All);

    [Fact]
    public void All_matches_the_backend_enum_values()
    {
        var names = new[] { "Cash", "Card", "Nequi", "Daviplata", "Breb", "BankTransfer", "BankDeposit", "Other" };
        for (var i = 0; i < names.Length; i++)
        {
            Assert.Equal(PaymentMethodLabels.All[i].Label, PaymentMethodLabels.Label(names[i]));
        }
    }
}
