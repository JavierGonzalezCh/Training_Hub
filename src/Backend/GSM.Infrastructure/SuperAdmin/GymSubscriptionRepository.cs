using GSM.Application.SuperAdmin.Abstractions;
using GSM.Domain.Gyms;
using GSM.Domain.SuperAdmin;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.SuperAdmin;

/// <summary>
/// Opera por encima del multi-tenancy: no hay Global Query Filter sobre <see cref="GymSubscription"/>
/// y la cartera se proyecta con un join explícito contra <c>gyms.gyms</c>.
/// </summary>
public sealed class GymSubscriptionRepository(GsmDbContext dbContext) : IGymSubscriptionRepository
{
    private DbSet<GymSubscription> Subscriptions => dbContext.Set<GymSubscription>();

    public void Add(GymSubscription subscription) => Subscriptions.Add(subscription);

    public Task<GymSubscription?> GetByGymIdAsync(Guid gymId, CancellationToken cancellationToken = default)
        => Subscriptions.FirstOrDefaultAsync(s => s.GymId == gymId, cancellationToken);

    public async Task<IReadOnlyList<PortfolioRow>> GetPortfolioAsync(CancellationToken cancellationToken = default)
        => await (
                from s in Subscriptions.AsNoTracking()
                join g in dbContext.Set<Gym>().IgnoreQueryFilters() on s.GymId equals g.Id
                select new PortfolioRow(
                    g.Id, g.Name, g.Slug, g.Status, s.MonthlyPrice,
                    s.StartedOn, s.TrialEndsOn, s.PaidThrough, s.SaasPlanId, s.MemberLimitOverride, s.TrialGraceDays, g.Type))
            .ToListAsync(cancellationToken);
}
