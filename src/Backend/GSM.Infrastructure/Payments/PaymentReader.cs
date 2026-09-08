using GSM.Application.Payments.Public;
using GSM.Domain.Payments;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Payments;

/// <summary>Lectura para otros módulos. Respeta el Global Query Filter (datos de tenant).</summary>
public sealed class PaymentReader(GsmDbContext dbContext) : IPaymentReader
{
    private IQueryable<Payment> Payments => dbContext.Set<Payment>();

    public async Task<decimal> GetMonthlyRevenueAsync(
        Guid gymId, int year, int month, CancellationToken cancellationToken = default)
    {
        var from = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddMonths(1);

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
