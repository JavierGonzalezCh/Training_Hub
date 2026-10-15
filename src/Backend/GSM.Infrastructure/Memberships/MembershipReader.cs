using GSM.Application.Common.Abstractions;
using GSM.Application.Memberships;
using GSM.Application.Memberships.Public;
using GSM.Domain.Memberships;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Memberships;

/// <summary>Lectura para otros módulos. Respeta el Global Query Filter (datos de tenant).</summary>
public sealed class MembershipReader(GsmDbContext dbContext, IClock clock) : IMembershipReader
{
    private IQueryable<Membership> Memberships => dbContext.Set<Membership>();

    private IQueryable<MembershipPlan> Plans => dbContext.Set<MembershipPlan>();

    public Task<ActiveMembershipView?> GetActiveAsync(
        Guid memberId, CancellationToken cancellationToken = default)
    {
        var today = clock.Today;

        return Memberships
            .Where(m => m.MemberId == memberId
                && m.Status == MembershipStatus.Active
                && m.StartDate <= today && today <= m.EndDate)
            .OrderByDescending(m => m.EndDate)
            .Join(Plans, m => m.PlanId, p => p.Id,
                (m, p) => new ActiveMembershipView(m.Id, p.Id, p.Name, m.StartDate, m.EndDate))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<MembershipSettingsView> GetSettingsAsync(Guid gymId, CancellationToken cancellationToken = default)
    {
        var row = await dbContext.Set<MembershipSettings>()
            .Where(s => s.GymId == gymId)
            .Select(s => new MembershipSettingsView(s.GraceDays, s.ExpiringDays))
            .FirstOrDefaultAsync(cancellationToken);

        return row ?? new MembershipSettingsView(
            MembershipSettings.DefaultGraceDays, MembershipSettings.DefaultExpiringDays);
    }

    public Task<AccessStatus> GetAccessStatusAsync(Guid memberId, CancellationToken cancellationToken = default)
        => GetAccessStatusOnAsync(memberId, clock.Today, cancellationToken);

    public async Task<AccessStatus> GetAccessStatusOnAsync(
        Guid memberId, DateOnly date, CancellationToken cancellationToken = default)
    {
        var rows = await Memberships
            .Where(m => m.MemberId == memberId && m.Status != MembershipStatus.Cancelled)
            .Select(m => new SpanRow(m.GymId, m.MemberId, m.Status, m.StartDate, m.EndDate))
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return CommercialStatusCalculator.Compute([], date, 0, 0);
        }

        var settings = await GetSettingsAsync(rows[0].GymId, cancellationToken);
        return CommercialStatusCalculator.Compute(
            rows.Select(r => new MembershipSpan(r.Status, r.Start, r.End)),
            date, settings.GraceDays, settings.ExpiringDays);
    }

    public async Task<IReadOnlyDictionary<Guid, CommercialStatus>> GetCommercialStatusesAsync(
        IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken = default)
    {
        if (memberIds.Count == 0)
        {
            return new Dictionary<Guid, CommercialStatus>();
        }

        var rows = await Memberships
            .Where(m => memberIds.Contains(m.MemberId) && m.Status != MembershipStatus.Cancelled)
            .Select(m => new SpanRow(m.GymId, m.MemberId, m.Status, m.StartDate, m.EndDate))
            .ToListAsync(cancellationToken);

        var computed = await ComputeAsync(rows, cancellationToken);
        return memberIds.Distinct().ToDictionary(
            id => id, id => computed.GetValueOrDefault(id, CommercialStatus.NoPlan));
    }

    public async Task<IReadOnlyCollection<Guid>> GetMemberIdsByStatusAsync(
        IReadOnlyCollection<CommercialStatus> statuses, CancellationToken cancellationToken = default)
    {
        var rows = await Memberships
            .Where(m => m.Status != MembershipStatus.Cancelled)
            .Select(m => new SpanRow(m.GymId, m.MemberId, m.Status, m.StartDate, m.EndDate))
            .ToListAsync(cancellationToken);

        var computed = await ComputeAsync(rows, cancellationToken);
        return computed.Where(kv => statuses.Contains(kv.Value)).Select(kv => kv.Key).ToList();
    }

    public async Task<IReadOnlyDictionary<CommercialStatus, int>> GetStatusCountsAsync(
        Guid gymId, IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken = default)
    {
        var rows = await Memberships
            .Where(m => m.GymId == gymId && m.Status != MembershipStatus.Cancelled)
            .Select(m => new SpanRow(m.GymId, m.MemberId, m.Status, m.StartDate, m.EndDate))
            .ToListAsync(cancellationToken);

        var computed = await ComputeAsync(rows, cancellationToken);
        var counts = Enum.GetValues<CommercialStatus>().ToDictionary(s => s, _ => 0);
        foreach (var id in memberIds.Distinct())
        {
            counts[computed.GetValueOrDefault(id, CommercialStatus.NoPlan)]++;
        }

        return counts;
    }

    public async Task<int> GetCountedMemberCountAsync(Guid gymId, CancellationToken cancellationToken = default)
    {
        var rows = await Memberships
            .Where(m => m.GymId == gymId && m.Status != MembershipStatus.Cancelled)
            .Select(m => new SpanRow(m.GymId, m.MemberId, m.Status, m.StartDate, m.EndDate))
            .ToListAsync(cancellationToken);

        var computed = await ComputeAsync(rows, cancellationToken);
        return computed.Count(kv => kv.Value.CountsTowardMemberLimit());
    }

    private sealed record SpanRow(Guid GymId, Guid MemberId, MembershipStatus Status, DateOnly Start, DateOnly End);

    private async Task<Dictionary<Guid, CommercialStatus>> ComputeAsync(
        List<SpanRow> rows, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var settingsByGym = new Dictionary<Guid, MembershipSettingsView>();
        var result = new Dictionary<Guid, CommercialStatus>();

        foreach (var member in rows.GroupBy(r => r.MemberId))
        {
            var gymId = member.First().GymId;
            if (!settingsByGym.TryGetValue(gymId, out var settings))
            {
                settings = await GetSettingsAsync(gymId, cancellationToken);
                settingsByGym[gymId] = settings;
            }

            result[member.Key] = CommercialStatusCalculator.Compute(
                member.Select(r => new MembershipSpan(r.Status, r.Start, r.End)),
                today, settings.GraceDays, settings.ExpiringDays).Commercial;
        }

        return result;
    }

    public Task<bool> BelongsToMemberAsync(
        Guid membershipId, Guid memberId, CancellationToken cancellationToken = default)
        => Memberships.AnyAsync(m => m.Id == membershipId && m.MemberId == memberId, cancellationToken);

    public async Task<DateOnly?> GetLatestEndDateAsync(Guid memberId, CancellationToken cancellationToken = default)
        => await Memberships.Where(m => m.MemberId == memberId && m.Status == MembershipStatus.Active)
            .Select(m => (DateOnly?)m.EndDate)
            .MaxAsync(cancellationToken);

    public Task<bool> IsActiveAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        var today = clock.Today;
        return Memberships.AnyAsync(
            m => m.MemberId == memberId
                && m.Status == MembershipStatus.Active
                && m.StartDate <= today && today <= m.EndDate,
            cancellationToken);
    }

    public async Task<IReadOnlyList<ExpiringMembershipView>> GetExpiringAsync(
        Guid gymId, int withinDays, CancellationToken cancellationToken = default)
    {
        var today = clock.Today;
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

    public Task<int> GetExpiredCountAsync(Guid gymId, CancellationToken cancellationToken = default)
    {
        var today = clock.Today;
        return Memberships.CountAsync(
            m => m.GymId == gymId && m.Status == MembershipStatus.Active && m.EndDate < today,
            cancellationToken);
    }

    public Task<int> GetExpiringCountAsync(
        Guid gymId, int withinDays, CancellationToken cancellationToken = default)
    {
        var today = clock.Today;
        var limit = today.AddDays(withinDays);
        return Memberships.CountAsync(
            m => m.GymId == gymId
                && m.Status == MembershipStatus.Active
                && m.EndDate >= today && m.EndDate <= limit,
            cancellationToken);
    }

    public Task<int> GetCoveredMemberCountAsync(DateOnly day, CancellationToken cancellationToken = default)
        => Memberships
            .Where(m => m.Status == MembershipStatus.Active && m.StartDate <= day && day <= m.EndDate)
            .Select(m => m.MemberId)
            .Distinct()
            .CountAsync(cancellationToken);
}
