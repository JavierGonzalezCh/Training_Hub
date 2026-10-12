using GSM.Application.Payments.Abstractions;
using GSM.Domain.Payments;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Payments;

public sealed class VoidRequestRepository(GsmDbContext dbContext) : IVoidRequestRepository
{
    private DbSet<VoidRequest> Requests => dbContext.Set<VoidRequest>();

    public void Add(VoidRequest request) => Requests.Add(request);

    public Task<VoidRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => Requests.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<VoidRequest?> GetPendingForPaymentAsync(Guid paymentId, CancellationToken cancellationToken = default)
        => Requests.FirstOrDefaultAsync(
            r => r.PaymentId == paymentId && r.Status == VoidRequestStatus.Pending, cancellationToken);

    public async Task<IReadOnlySet<Guid>> GetPendingPaymentIdsAsync(
        IReadOnlyCollection<Guid> paymentIds, CancellationToken cancellationToken = default)
    {
        if (paymentIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var ids = await Requests
            .Where(r => r.Status == VoidRequestStatus.Pending && paymentIds.Contains(r.PaymentId))
            .Select(r => r.PaymentId)
            .ToListAsync(cancellationToken);
        return ids.ToHashSet();
    }

    public async Task<(IReadOnlyList<VoidRequest> Items, int Total)> GetPageAsync(
        VoidRequestStatus? status, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var q = Requests.AsNoTracking().AsQueryable();
        if (status is { } s)
        {
            q = q.Where(r => r.Status == s);
        }

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderBy(r => r.Status == VoidRequestStatus.Pending ? 0 : 1)
            .ThenByDescending(r => r.RequestedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }
}
