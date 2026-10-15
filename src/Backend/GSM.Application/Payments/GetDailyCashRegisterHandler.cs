using GSM.Application.Common.Abstractions;
using GSM.Application.Payments.Abstractions;
using GSM.Application.Payments.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Identity;

namespace GSM.Application.Payments;

/// <summary>
/// Cierre diario del Owner (Payments v2 §8): total, totales por método con cantidad (para conciliar), los turnos del día
/// con sus diferencias, los movimientos de caja, las anulaciones con su motivo y la lista completa de pagos.
/// El día se agrupa con <see cref="IClock.BusinessDayRange"/>.
/// </summary>
public sealed class GetDailyCashRegisterHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IPaymentRepository payments,
    IVoidRequestRepository voidRequests,
    ICashShiftRepository shifts,
    CashDrawerService drawer)
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
        var ids = dayPayments.Select(p => p.Id).ToList();

        var reversed = await payments.GetReversedIdsAsync(ids, cancellationToken);
        var pending = await voidRequests.GetPendingPaymentIdsAsync(ids, cancellationToken);

        var totalsByMethod = dayPayments
            .GroupBy(p => p.Method.ToString())
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

        var responses = await drawer.ToPaymentResponsesAsync(dayPayments, reversed, pending, cancellationToken);

        var dayShifts = await shifts.GetOpenedInRangeAsync(fromUtc, toUtc, cancellationToken);
        var movements = await shifts.GetMovementsAsync(dayShifts.Select(s => s.Id).ToList(), cancellationToken);
        var names = await drawer.ResolveNamesAsync(
            dayShifts.SelectMany(s => new[] { s.OpenedBy, s.ClosedBy ?? Guid.Empty })
                .Concat(movements.Select(m => m.CreatedBy)),
            cancellationToken);

        var shiftRows = new List<CashShiftResponse>(dayShifts.Count);
        foreach (var shift in dayShifts)
        {
            var expected = shift.IsOpen ? await drawer.ComputeExpectedAsync(shift, cancellationToken) : shift.ExpectedCash;
            shiftRows.Add(drawer.ToResponse(shift, names, expected));
        }

        return new DailyCashRegisterResponse(
            day,
            dayPayments.Sum(p => p.Amount),
            dayPayments.Count,
            totalsByMethod,
            responses)
        {
            Methods = CashDrawerService.SummarizeByMethod(dayPayments),
            Shifts = shiftRows,
            Movements = movements.Select(m => CashDrawerService.ToResponse(m, names)).ToList(),
            Reversals = responses.Where(r => r.ReversesPaymentId is not null).ToList(),
        };
    }
}
