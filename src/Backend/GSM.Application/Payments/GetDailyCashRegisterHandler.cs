using GSM.Application.Common.Abstractions;
using GSM.Application.Payments.Abstractions;
using GSM.Application.Payments.Contracts;

namespace GSM.Application.Payments;

/// <summary>Cierre de caja de un día: total, desglose por método, cantidad de transacciones y lista completa.</summary>
public sealed class GetDailyCashRegisterHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IPaymentRepository payments)
{
    public async Task<DailyCashRegisterResponse> HandleAsync(
        DateOnly? date, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var day = date ?? clock.Today;
        var (fromUtc, toUtc) = clock.BusinessDayRange(day);

        var dayPayments = await payments.GetInRangeAsync(fromUtc, toUtc, cancellationToken);

        var reversed = await payments.GetReversedIdsAsync(dayPayments.Select(p => p.Id).ToList(), cancellationToken);

        var totalsByMethod = dayPayments
            .GroupBy(p => p.Method.ToString())
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

        return new DailyCashRegisterResponse(
            day,
            dayPayments.Sum(p => p.Amount),
            dayPayments.Count,
            totalsByMethod,
            dayPayments.Select(p => PaymentMapper.ToResponse(p, reversed)).ToList());
    }
}
