using GSM.Application.Payments.Abstractions;
using GSM.Domain.Payments;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Payments;

public sealed class PaymentRepository(GsmDbContext dbContext) : IPaymentRepository
{
    private DbSet<Payment> Payments => dbContext.Set<Payment>();

    public void Add(Payment payment) => Payments.Add(payment);

    public Task<Payment?> GetByIdAsync(Guid paymentId, CancellationToken cancellationToken = default)
        => Payments.FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken);

    public Task<bool> IsReversedAsync(Guid paymentId, CancellationToken cancellationToken = default)
        => Payments.AnyAsync(p => p.ReversesPaymentId == paymentId, cancellationToken);

    public async Task<IReadOnlySet<Guid>> GetReversedIdsAsync(
        IReadOnlyCollection<Guid> paymentIds, CancellationToken cancellationToken = default)
    {
        if (paymentIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var reversed = await Payments
            .Where(p => p.ReversesPaymentId != null && paymentIds.Contains(p.ReversesPaymentId.Value))
            .Select(p => p.ReversesPaymentId!.Value)
            .ToListAsync(cancellationToken);

        return reversed.ToHashSet();
    }

    public async Task<(IReadOnlyList<Payment> Items, int Total)> GetPageAsync(
        PaymentPageQuery query, CancellationToken cancellationToken = default)
    {
        var q = Payments.AsQueryable();

        if (query.FromUtc is { } from)
        {
            q = q.Where(p => p.PaidAt >= from);
        }

        if (query.ToUtc is { } to)
        {
            q = q.Where(p => p.PaidAt < to);
        }

        if (query.Method is { } method)
        {
            q = q.Where(p => p.Method == method);
        }

        if (query.MemberId is { } memberId)
        {
            q = q.Where(p => p.MemberId == memberId);
        }

        var total = await q.CountAsync(cancellationToken);

        var items = await q
            .OrderByDescending(p => p.PaidAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<IReadOnlyList<Payment>> GetInRangeAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
        => await Payments
            .Where(p => p.PaidAt >= fromUtc && p.PaidAt < toUtc)
            .OrderBy(p => p.PaidAt)
            .ToListAsync(cancellationToken);
}
