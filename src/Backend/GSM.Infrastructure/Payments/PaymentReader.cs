using GSM.Application.Common.Abstractions;
using GSM.Application.Payments.Public;
using GSM.Domain.Payments;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Payments;

/// <summary>Lectura para otros módulos. Respeta el Global Query Filter (datos de tenant).</summary>
public sealed class PaymentReader(GsmDbContext dbContext, IClock clock) : IPaymentReader
{
    private IQueryable<Payment> Payments => dbContext.Set<Payment>();

    public async Task<decimal> GetMonthlyRevenueAsync(
        Guid gymId, int year, int month, CancellationToken cancellationToken = default)
    {
        // El mes es un periodo de negocio: va de la medianoche local del día 1 a la del día 1
        // del mes siguiente. Con un rango en UTC, los pagos de las últimas horas del último día
        // caerían en el mes siguiente (Colombia es UTC-5).
        var firstDay = new DateOnly(year, month, 1);
        var from = clock.BusinessDayRange(firstDay).Start;
        var to = clock.BusinessDayRange(firstDay.AddMonths(1)).Start;

        return await Payments
            .Where(p => p.GymId == gymId && p.PaidAt >= from && p.PaidAt < to)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;
    }

    public async Task<IReadOnlyList<MemberPaymentView>> GetRecentByMemberAsync(
        Guid memberId, int limit, CancellationToken cancellationToken = default)
        => await Payments
            .Where(p => p.MemberId == memberId)
            .OrderByDescending(p => p.PaidAt)
            .Take(Math.Clamp(limit, 1, 50))
            .Select(p => new MemberPaymentView(p.Id, p.Amount, p.Method, p.Concept, p.PaidAt))
            .ToListAsync(cancellationToken);
}
