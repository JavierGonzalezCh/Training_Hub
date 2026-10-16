using GSM.Application.Memberships.Abstractions;
using GSM.Application.Payments.Public;

namespace GSM.Infrastructure.Payments;

/// <summary>Payments cumple el puerto de Memberships para el crédito del cambio de plan.</summary>
public sealed class PaymentsMembershipPaidAmountProvider(IPaymentReader payments) : IMembershipPaidAmountProvider
{
    public Task<decimal> GetNetPaidAsync(Guid membershipId, CancellationToken cancellationToken = default)
        => payments.GetNetPaidForMembershipAsync(membershipId, cancellationToken);
}
