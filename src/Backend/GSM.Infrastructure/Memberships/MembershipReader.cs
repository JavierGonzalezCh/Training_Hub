using GSM.Application.Memberships.Public;
using GSM.Domain.Memberships;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Memberships;

/// <summary>Lectura para otros módulos. Respeta el Global Query Filter (datos de tenant).</summary>
public sealed class MembershipReader(GsmDbContext dbContext) : IMembershipReader
{
    private IQueryable<Membership> Memberships => dbContext.Set<Membership>();

    private IQueryable<MembershipPlan> Plans => dbContext.Set<MembershipPlan>();

    public Task<ActiveMembershipView?> GetActiveAsync(
        Guid memberId, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return Memberships
            .Where(m => m.MemberId == memberId
                && m.Status == MembershipStatus.Active
                && m.StartDate <= today && today <= m.EndDate)
            .OrderByDescending(m => m.EndDate)
            .Join(Plans, m => m.PlanId, p => p.Id,
                (m, p) => new ActiveMembershipView(m.Id, p.Id, p.Name, m.StartDate, m.EndDate))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<bool> IsActiveAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return Memberships.AnyAsync(
            m => m.MemberId == memberId
                && m.Status == MembershipStatus.Active
                && m.StartDate <= today && today <= m.EndDate,
            cancellationToken);
    }

    public async Task<IReadOnlyList<ExpiringMembershipView>> GetExpiringAsync(
        Guid gymId, int withinDays, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var limit = today.AddDays(withinDays);

        return await Memberships
            .Where(m => m.GymId == gymId
                && m.Status == MembershipStatus.Active
                && m.EndDate >= today && m.EndDate <= limit)
            .OrderBy(m => m.EndDate)
            .Join(Plans, m => m.PlanId, p => p.Id,
                (m, p) => new { m.Id, m.MemberId, p.Name, m.EndDate })
            .Select(x => new ExpiringMembershipView(
                x.Id, x.MemberId, x.Name, x.EndDate, x.EndDate.DayNumber - today.DayNumber))
            .ToListAsync(cancellationToken);
    }
}
