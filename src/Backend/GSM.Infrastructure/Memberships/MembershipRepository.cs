using GSM.Application.Memberships;
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
            .Where(m => m.MemberId == memberId && m.Status == MembershipStatus.Active && m.StartDate > date
                && m.EntriesTotal == null)
            .OrderBy(m => m.StartDate)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ExpiringMembershipRow>> GetExpiringAsync(
        Guid gymId, DateOnly today, int withinDays, CancellationToken cancellationToken = default)
    {
        var limit = today.AddDays(withinDays);
        var freezes = Freezes;
        var usages = PassUsages;

        // Una membresía congelada hoy no está "por vencer": su fin se corre al reanudar.
        return await Memberships
            .Where(m => m.Status == MembershipStatus.Active
                && m.EndDate >= today
                && (m.EndDate <= limit
                    || (m.EntriesTotal != null
                        && m.EntriesTotal.Value - usages.Count(u => u.MembershipId == m.Id) <= CommercialStatusCalculator.PassLowEntries))
                && !freezes.Any(f => f.MembershipId == m.Id && f.StartsOn <= today && (f.EndsOn == null || f.EndsOn >= today)))
            .OrderBy(m => m.EndDate)
            .Join(Plans, m => m.PlanId, p => p.Id,
                (m, p) => new ExpiringMembershipRow(m.Id, m.MemberId, p.Id, p.Name, m.EndDate))
            .ToListAsync(cancellationToken);
    }

    public void Add(Membership membership) => Memberships.Add(membership);

    private DbSet<MembershipFreeze> Freezes => dbContext.Set<MembershipFreeze>();

    public async Task<IReadOnlyList<MembershipFreeze>> GetFreezesAsync(
        IReadOnlyCollection<Guid> membershipIds, CancellationToken cancellationToken = default)
    {
        if (membershipIds.Count == 0)
        {
            return [];
        }

        return await Freezes
            .Where(f => membershipIds.Contains(f.MembershipId))
            .OrderBy(f => f.StartsOn)
            .ToListAsync(cancellationToken);
    }

    public Task<MembershipFreeze?> GetFreezeByIdAsync(Guid freezeId, CancellationToken cancellationToken = default)
        => Freezes.FirstOrDefaultAsync(f => f.Id == freezeId, cancellationToken);

    public void Add(MembershipFreeze freeze) => Freezes.Add(freeze);

    public void Remove(MembershipFreeze freeze) => Freezes.Remove(freeze);

    private DbSet<PassUsage> PassUsages => dbContext.Set<PassUsage>();

    public Task<PassUsage?> GetPassUsageByAttendanceAsync(Guid attendanceId, CancellationToken cancellationToken = default)
        => PassUsages.FirstOrDefaultAsync(u => u.AttendanceId == attendanceId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> GetPassUsedCountsAsync(
        IReadOnlyCollection<Guid> membershipIds, CancellationToken cancellationToken = default)
    {
        if (membershipIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        return await PassUsages
            .Where(u => membershipIds.Contains(u.MembershipId))
            .GroupBy(u => u.MembershipId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
    }

    public async Task<IReadOnlyList<PassUsage>> GetPassUsagesAsync(
        IReadOnlyCollection<Guid> membershipIds, CancellationToken cancellationToken = default)
    {
        if (membershipIds.Count == 0)
        {
            return [];
        }

        return await PassUsages
            .Where(u => membershipIds.Contains(u.MembershipId))
            .OrderBy(u => u.UsedOn)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Membership>> GetPassesCoveringAsync(
        Guid memberId, DateOnly date, CancellationToken cancellationToken = default)
        => await Memberships
            .Where(m => m.MemberId == memberId && m.Status == MembershipStatus.Active
                && m.EntriesTotal != null && m.StartDate <= date && date <= m.EndDate)
            .OrderBy(m => m.EndDate)
            .ToListAsync(cancellationToken);

    public Task<bool> HasTimeMembershipCoveringAsync(
        Guid memberId, DateOnly date, CancellationToken cancellationToken = default)
    {
        var freezes = Freezes;
        return Memberships.AnyAsync(
            m => m.MemberId == memberId && m.Status == MembershipStatus.Active && m.EntriesTotal == null
                && m.StartDate <= date && date <= m.EndDate
                && !freezes.Any(f => f.MembershipId == m.Id && f.StartsOn <= date && (f.EndsOn == null || f.EndsOn >= date)),
            cancellationToken);
    }

    public void Add(PassUsage usage) => PassUsages.Add(usage);

    public void Remove(PassUsage usage) => PassUsages.Remove(usage);

    public Task<MembershipGroup?> GetGroupByIdAsync(Guid groupId, CancellationToken cancellationToken = default)
        => dbContext.Set<MembershipGroup>().FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);

    public async Task<IReadOnlyList<Membership>> GetByGroupAsync(Guid groupId, CancellationToken cancellationToken = default)
        => await Memberships
            .Where(m => m.GroupId == groupId)
            .OrderByDescending(m => m.IsGroupHolder)
            .ThenBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Membership>> GetActiveOverlappingAsync(
        Guid memberId, DateOnly start, DateOnly end, CancellationToken cancellationToken = default)
        => await Memberships
            .Where(m => m.MemberId == memberId && m.Status == MembershipStatus.Active && m.EntriesTotal == null
                && m.StartDate <= end && m.EndDate >= start)
            .OrderBy(m => m.StartDate)
            .ToListAsync(cancellationToken);

    public void Add(MembershipGroup group) => dbContext.Set<MembershipGroup>().Add(group);

    public Task<MembershipSettings?> GetSettingsAsync(Guid gymId, CancellationToken cancellationToken = default)
        => dbContext.Set<MembershipSettings>().FirstOrDefaultAsync(s => s.GymId == gymId, cancellationToken);

    public void Add(MembershipSettings settings) => dbContext.Set<MembershipSettings>().Add(settings);

    public async Task ReplaceReasonsForMemberAsync(
        Guid gymId, Guid memberId, string replacement, CancellationToken cancellationToken = default)
    {
        await Memberships.IgnoreQueryFilters()
            .Where(m => m.GymId == gymId && m.MemberId == memberId
                        && m.CancellationReason != null && m.CancellationReason != replacement)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.CancellationReason, replacement), cancellationToken);

        var membershipIds = Memberships.IgnoreQueryFilters()
            .Where(m => m.GymId == gymId && m.MemberId == memberId)
            .Select(m => m.Id);
        await dbContext.Set<MembershipFreeze>().IgnoreQueryFilters()
            .Where(f => f.GymId == gymId && membershipIds.Contains(f.MembershipId) && f.Reason != replacement)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.Reason, replacement), cancellationToken);
    }
}
