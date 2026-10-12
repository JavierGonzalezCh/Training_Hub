using GSM.Application.Common.Abstractions;
using GSM.Application.Payments.Abstractions;
using GSM.Application.Payments.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Identity;

namespace GSM.Application.Payments;

/// <summary>Cierre de caja de un día: total, desglose por método, cantidad de transacciones y lista completa.</summary>
public sealed class GetDailyCashRegisterHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IPaymentRepository payments,
    IVoidRequestRepository voidRequests)
{
    public async Task<DailyCashRegisterResponse> HandleAsync(
        DateOnly? date, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        if (!currentUser.HasPermission(StaffPermission.ViewFinances))
        {
            throw new ForbiddenDomainException("No tienes permiso para ver el cierre de caja.");
        }

        var day = date ?? clock.Today;
        var (fromUtc, toUtc) = clock.BusinessDayRange(day);

        var dayPayments = await payments.GetInRangeAsync(fromUtc, toUtc, cancellationToken);

        var reversed = await payments.GetReversedIdsAsync(dayPayments.Select(p => p.Id).ToList(), cancellationToken);

        var pending = await voidRequests.GetPendingPaymentIdsAsync(dayPayments.Select(p => p.Id).ToList(), cancellationToken);

        var totalsByMethod = dayPayments
            .GroupBy(p => p.Method.ToString())
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

        return new DailyCashRegisterResponse(
            day,
            dayPayments.Sum(p => p.Amount),
            dayPayments.Count,
            totalsByMethod,
            dayPayments.Select(p => PaymentMapper.ToResponse(p, reversed, pending)).ToList());
    }
}
