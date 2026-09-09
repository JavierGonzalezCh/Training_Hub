using GSM.Application.Gyms.Public;
using GSM.Application.SuperAdmin.Abstractions;
using GSM.Application.SuperAdmin.Contracts;
using GSM.Domain.Common.Exceptions;

namespace GSM.Application.SuperAdmin;

/// <summary>Histórico de pagos al SaaS de un gym.</summary>
public sealed class ListGymSubscriptionPaymentsHandler(
    IGymReader gyms,
    ISubscriptionPaymentRepository payments)
{
    public async Task<IReadOnlyList<SubscriptionPaymentItem>> HandleAsync(
        Guid gymId, CancellationToken cancellationToken)
    {
        if (!await gyms.ExistsAsync(gymId, cancellationToken))
        {
            throw new NotFoundException("El gym no existe.");
        }

        var rows = await payments.GetByGymAsync(gymId, cancellationToken);
        return rows.Select(SubscriptionMapper.ToItem).ToList();
    }
}
