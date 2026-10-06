using GSM.Application.Memberships.Abstractions;
using GSM.Domain.Memberships;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Memberships;

public sealed class MembershipRepository(GsmDbContext dbContext) : IMembershipRepository
{
    private DbSet<MembershipPlan> Plans => dbContext.Set<MembershipPlan>();

    private DbSet<Membership> Memberships => dbContext.Set<Membership>();

    public Task<MembershipPlan?> GetPlanByIdAsync(Guid planId, CancellationToken cancellationToken = default)
        => Plans.FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);

    public async Task<IReadOnlyList<MembershipPlan>> ListPlansAsync(
        bool includeInactive, CancellationToken cancellationToken = default)
        => await Plans
            .Where(p => includeInactive || p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

    public Task<bool> PlanNameExistsAsync(
        string name, Guid? excludePlanId, CancellationToken cancellationToken = default)
        => Plans.AnyAsync(
            p => p.Name == name && (excludePlanId == null || p.Id != excludePlanId), cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> GetPlanNamesAsync(
        IReadOnlyCollection<Guid> planIds, CancellationToken cancellationToken = default)
    {
        if (planIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await Plans
            .Where(p => planIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);
    }

    public void Add(MembershipPlan plan) => Plans.Add(plan);

    public Task<Membership?> GetByIdAsync(Guid membershipId, CancellationToken cancellationToken = default)
        => Memberships.FirstOrDefaultAsync(m => m.Id == membershipId, cancellationToken);

    public async Task<IReadOnlyList<Membership>> GetHistoryAsync(
        Guid memberId, CancellationToken cancellationToken = default)
        => await Memberships
            .Where(m => m.MemberId == memberId)
            .OrderByDescending(m => m.StartDate)
            .ToListAsync(cancellationToken);

    public Task<Membership?> GetLatestOpenAsync(
        Guid memberId, DateOnly today, CancellationToken cancellationToken = default)
        => Memberships
            .Where(m => m.MemberId == memberId
                && (m.Status == MembershipStatus.Active || m.Status == MembershipStatus.Paused)
                && m.EndDate >= today)
            .OrderByDescending(m => m.EndDate)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Membership>> GetActiveStartingAfterAsync(
        Guid memberId, DateOnly date, CancellationToken cancellationToken = default)
        => await Memberships
            .Where(m => m.MemberId == memberId && m.Status == MembershipStatus.Active && m.StartDate > date)
            .OrderBy(m => m.StartDate)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ExpiringMembershipRow>> GetExpiringAsync(
        Guid gymId, DateOnly today, int withinDays, CancellationToken cancellationToken = default)
    {
        var limit = today.AddDays(withinDays);

        return await Memberships
            .Where(m => m.Status == MembershipStatus.Active
                && m.EndDate >= today
                && m.EndDate <= limit)
            .OrderBy(m => m.EndDate)
            .Join(Plans, m => m.PlanId, p => p.Id,
                (m, p) => new ExpiringMembershipRow(m.Id, m.MemberId, p.Id, p.Name, m.EndDate))
            .ToListAsync(cancellationToken);
    }

    public void Add(Membership membership) => Memberships.Add(membership);
}
