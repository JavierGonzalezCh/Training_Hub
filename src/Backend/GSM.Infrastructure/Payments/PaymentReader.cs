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

    public async Task<decimal> GetRevenueBetweenDaysAsync(
        Guid gymId, DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken = default)
    {
        var from = clock.BusinessDayRange(firstDay).Start;
        var to = clock.BusinessDayRange(lastDay).End;

        return await Payments
            .Where(p => p.GymId == gymId && p.PaidAt >= from && p.PaidAt < to)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;
    }

    public async Task<IReadOnlyDictionary<Guid, decimal>> GetMonthlyRevenueByGymAsync(
        int year, int month, CancellationToken cancellationToken = default)
    {
        // Mismo periodo de negocio que GetMonthlyRevenueAsync: de la medianoche local del día 1 a la
        // del día 1 del mes siguiente, nunca un rango UTC.
        var firstDay = new DateOnly(year, month, 1);
        var from = clock.BusinessDayRange(firstDay).Start;
        var to = clock.BusinessDayRange(firstDay.AddMonths(1)).Start;

        return await Payments
            .Where(p => p.PaidAt >= from && p.PaidAt < to)
            .GroupBy(p => p.GymId)
            .Select(g => new { GymId = g.Key, Total = g.Sum(p => p.Amount) })
            .ToDictionaryAsync(x => x.GymId, x => x.Total, cancellationToken);
    }

    public async Task<decimal> GetNetPaidForMembershipAsync(
        Guid membershipId, CancellationToken cancellationToken = default)
        => await Payments
            .Where(p => p.MembershipId == membershipId)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;

    public async Task<IReadOnlyList<MemberPaymentView>> GetRecentByMemberAsync(
        Guid memberId, int limit, CancellationToken cancellationToken = default)
        => await Payments
            .Where(p => p.MemberId == memberId)
            .OrderByDescending(p => p.PaidAt)
            .Take(Math.Clamp(limit, 1, 50))
            .Select(p => new MemberPaymentView(p.Id, p.Amount, p.Method, p.Concept, p.PaidAt))
            .ToListAsync(cancellationToken);
}
