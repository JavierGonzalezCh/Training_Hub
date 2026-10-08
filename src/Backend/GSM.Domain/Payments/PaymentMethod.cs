namespace GSM.Domain.Payments;

/// <summary>
/// Método de pago de Colombia. En Postgres es el enum <c>payments.payment_method</c> con
/// <c>SnakeCaseNameTranslator</c> (<c>bank_transfer</c>, <c>bank_deposit</c>, …). Solo <see cref="Cash"/> va
/// a la caja del turno; el resto los concilia el Owner contra sus extractos.
/// </summary>
public enum PaymentMethod
{
    Cash,
    Card,
    Nequi,
    Daviplata,
    Breb,
    BankTransfer,
    BankDeposit,
    Other,
}

public static class PaymentMethodExtensions
{
    /// <summary>¿El dinero entra físicamente a la caja del turno? Solo el efectivo.</summary>
    public static bool GoesToCashDrawer(this PaymentMethod method) => method == PaymentMethod.Cash;
}
