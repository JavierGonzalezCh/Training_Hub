using GSM.Application.Common.Abstractions;
using GSM.Application.SuperAdmin.Abstractions;
using GSM.Application.SuperAdmin.Contracts;

namespace GSM.Application.SuperAdmin;

/// <summary>
/// Todos los cobros del SaaS en un periodo, de todos los gyms. El periodo se agrupa por
/// <b>día de negocio</b> (<see cref="IClock.BusinessDayRange"/>), no por rango UTC.
/// </summary>
public sealed class ListPlatformPaymentsHandler(
    IClock clock,
    ISubscriptionPaymentRepository payments)
{
    public async Task<PlatformPaymentsResponse> HandleAsync(
        DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var fromDay = from ?? new DateOnly(today.Year, today.Month, 1);
        var toDay = to ?? today;

        if (toDay < fromDay)
        {
            (fromDay, toDay) = (toDay, fromDay);
        }

        var fromUtc = clock.BusinessDayRange(fromDay).Start;
        var toUtc = clock.BusinessDayRange(toDay).End;

        var rows = await payments.GetInRangeAsync(fromUtc, toUtc, cancellationToken);

        return new PlatformPaymentsResponse(
            rows.Select(SubscriptionMapper.ToItem).ToList(),
            rows.Sum(r => r.Amount),
            fromDay,
            toDay);
    }
}
