using GSM.Application.SuperAdmin.Abstractions;
using GSM.Domain.Gyms;
using GSM.Domain.SuperAdmin;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.SuperAdmin;

/// <summary>Opera por encima del multi-tenancy; proyección explícita, nunca materializa entidades para listar.</summary>
public sealed class SubscriptionPaymentRepository(GsmDbContext dbContext) : ISubscriptionPaymentRepository
{
    private DbSet<SubscriptionPayment> Payments => dbContext.Set<SubscriptionPayment>();

    public void Add(SubscriptionPayment payment) => Payments.Add(payment);

    public async Task<IReadOnlyList<SubscriptionPaymentRow>> GetByGymAsync(
        Guid gymId, CancellationToken cancellationToken = default)
        => await Payments.AsNoTracking()
            .Where(p => p.GymId == gymId)
            .OrderByDescending(p => p.PaidAt)
            .Select(p => new SubscriptionPaymentRow(
                p.Id, p.GymId, p.Amount, p.Method, p.CoversFrom, p.CoversThrough,
                p.PaidAt, p.RegisteredBy, p.Reference, p.Notes))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PlatformPaymentRow>> GetInRangeAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
        => await (
                from p in Payments.AsNoTracking()
                join g in dbContext.Set<Gym>().IgnoreQueryFilters() on p.GymId equals g.Id
                where p.PaidAt >= fromUtc && p.PaidAt < toUtc
                orderby p.PaidAt descending
                select new PlatformPaymentRow(
                    p.Id, p.GymId, g.Name, p.Amount, p.Method, p.CoversFrom, p.CoversThrough,
                    p.PaidAt, p.RegisteredBy))
            .ToListAsync(cancellationToken);

    public async Task<decimal> SumInRangeAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
        => await Payments.AsNoTracking()
            .Where(p => p.PaidAt >= fromUtc && p.PaidAt < toUtc)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;
}
