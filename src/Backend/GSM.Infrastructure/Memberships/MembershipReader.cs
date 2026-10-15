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

    /// <summary>Membresías que no caen dentro de un congelamiento ese día (un día congelado no está cubierto).</summary>
    private IQueryable<Membership> NotFrozenOn(DateOnly day)
    {
        var freezes = dbContext.Set<MembershipFreeze>();
        return Memberships.Where(m => !freezes.Any(
            f => f.MembershipId == m.Id && f.StartsOn <= day && (f.EndsOn == null || f.EndsOn >= day)));
    }

    private async Task<IReadOnlyList<MembershipFreeze>> LoadFreezesAsync(
        IEnumerable<Guid> membershipIds, CancellationToken cancellationToken)
    {
        var ids = membershipIds.Distinct().ToList();
        return ids.Count == 0
            ? []
            : await dbContext.Set<MembershipFreeze>()
                .Where(f => ids.Contains(f.MembershipId))
                .ToListAsync(cancellationToken);
    }

    private IQueryable<MembershipPlan> Plans => dbContext.Set<MembershipPlan>();

    public Task<ActiveMembershipView?> GetActiveAsync(
        Guid memberId, CancellationToken cancellationToken = default)
    {
        var today = clock.Today;

        return NotFrozenOn(today)
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
            .Select(m => new SpanRow(m.Id, m.GymId, m.MemberId, m.Status, m.StartDate, m.EndDate))
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return CommercialStatusCalculator.Compute([], date, 0, 0);
        }

        var freezes = await LoadFreezesAsync(rows.Select(r => r.Id), cancellationToken);
        var settings = await GetSettingsAsync(rows[0].GymId, cancellationToken);
        return CommercialStatusCalculator.Compute(
            rows.Select(r => new MembershipSpan(r.Status, r.Start, r.End, FreezesOf(freezes, r.Id))),
            date, settings.GraceDays, settings.ExpiringDays);
    }

    public async Task<IReadOnlyCollection<Guid>> GetMemberIdsByPlansAsync(
        IReadOnlyCollection<Guid> planIds, CancellationToken cancellationToken = default)
    {
        if (planIds.Count == 0)
        {
            return [];
        }

        var today = clock.Today;
        return await NotFrozenOn(today)
            .Where(m => planIds.Contains(m.PlanId)
                && m.Status == MembershipStatus.Active
                && m.StartDate <= today && today <= m.EndDate)
            .Select(m => m.MemberId)
            .Distinct()
            .ToListAsync(cancellationToken);
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
            .Select(m => new SpanRow(m.Id, m.GymId, m.MemberId, m.Status, m.StartDate, m.EndDate))
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
            .Select(m => new SpanRow(m.Id, m.GymId, m.MemberId, m.Status, m.StartDate, m.EndDate))
            .ToListAsync(cancellationToken);

        var computed = await ComputeAsync(rows, cancellationToken);
        return computed.Where(kv => statuses.Contains(kv.Value)).Select(kv => kv.Key).ToList();
    }

    public async Task<IReadOnlyDictionary<CommercialStatus, int>> GetStatusCountsAsync(
        Guid gymId, IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken = default)
    {
        var rows = await Memberships
            .Where(m => m.GymId == gymId && m.Status != MembershipStatus.Cancelled)
            .Select(m => new SpanRow(m.Id, m.GymId, m.MemberId, m.Status, m.StartDate, m.EndDate))
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
            .Select(m => new SpanRow(m.Id, m.GymId, m.MemberId, m.Status, m.StartDate, m.EndDate))
            .ToListAsync(cancellationToken);

        var computed = await ComputeAsync(rows, cancellationToken);
        return computed.Count(kv => kv.Value.CountsTowardMemberLimit());
    }

    private static IReadOnlyList<FreezeSpan> FreezesOf(IReadOnlyList<MembershipFreeze> freezes, Guid membershipId)
        => freezes.Where(f => f.MembershipId == membershipId).Select(f => new FreezeSpan(f.StartsOn, f.EndsOn)).ToList();

    private sealed record SpanRow(Guid Id, Guid GymId, Guid MemberId, MembershipStatus Status, DateOnly Start, DateOnly End);

    private async Task<Dictionary<Guid, CommercialStatus>> ComputeAsync(
        List<SpanRow> rows, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var freezes = await LoadFreezesAsync(rows.Select(r => r.Id), cancellationToken);
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
                member.Select(r => new MembershipSpan(r.Status, r.Start, r.End, FreezesOf(freezes, r.Id))),
                today, settings.GraceDays, settings.ExpiringDays).Commercial;
        }

        return result;
    }

    public Task<bool> BelongsToMemberAsync(
        Guid membershipId, Guid memberId, CancellationToken cancellationToken = default)
        => Memberships.AnyAsync(m => m.Id == membershipId && m.MemberId == memberId, cancellationToken);

    public async Task<DateOnly?> GetLatestEndDateAsync(Guid memberId, CancellationToken cancellationToken = default)
        => await NotFrozenOn(clock.Today).Where(m => m.MemberId == memberId && m.Status == MembershipStatus.Active)
            .Select(m => (DateOnly?)m.EndDate)
            .MaxAsync(cancellationToken);

    public Task<bool> IsActiveAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        var today = clock.Today;
        return NotFrozenOn(today).AnyAsync(
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

        return await NotFrozenOn(today)
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

    public async Task<IReadOnlyList<ReminderMembershipView>> GetReminderCandidatesAsync(
        Guid gymId, int daysBefore, CancellationToken cancellationToken = default)
    {
        var today = clock.Today;
        var graceDays = (await GetSettingsAsync(gymId, cancellationToken)).GraceDays;
        var from = today.AddDays(-graceDays);
        var to = today.AddDays(daysBefore);

        return await NotFrozenOn(today)
            .Where(m => m.GymId == gymId
                && m.Status == MembershipStatus.Active
                && m.EndDate >= from && m.EndDate <= to)
            .OrderBy(m => m.EndDate)
            .Join(Plans, m => m.PlanId, p => p.Id,
                (m, p) => new { m.Id, m.MemberId, p.Name, m.EndDate })
            .Select(x => new ReminderMembershipView(
                x.Id, x.MemberId, x.Name, x.EndDate, x.EndDate.DayNumber - today.DayNumber, graceDays))
            .ToListAsync(cancellationToken);
    }

    public Task<int> GetExpiredCountAsync(Guid gymId, CancellationToken cancellationToken = default)
    {
        var today = clock.Today;
        return NotFrozenOn(today).CountAsync(
            m => m.GymId == gymId && m.Status == MembershipStatus.Active && m.EndDate < today,
            cancellationToken);
    }

    public Task<int> GetExpiringCountAsync(
        Guid gymId, int withinDays, CancellationToken cancellationToken = default)
    {
        var today = clock.Today;
        var limit = today.AddDays(withinDays);
        return NotFrozenOn(today).CountAsync(
            m => m.GymId == gymId
                && m.Status == MembershipStatus.Active
                && m.EndDate >= today && m.EndDate <= limit,
            cancellationToken);
    }

    public Task<int> GetCoveredMemberCountAsync(DateOnly day, CancellationToken cancellationToken = default)
        => NotFrozenOn(day)
            .Where(m => m.Status == MembershipStatus.Active && m.StartDate <= day && day <= m.EndDate)
            .Select(m => m.MemberId)
            .Distinct()
            .CountAsync(cancellationToken);

    public async Task<int> GetCoveredDaysAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        var today = clock.Today;
        var rows = await Memberships
            .Where(m => m.MemberId == memberId && m.Status != MembershipStatus.Cancelled)
            .ToListAsync(cancellationToken);
        var freezes = await LoadFreezesAsync(rows.Select(r => r.Id), cancellationToken);
        return rows.Sum(m => m.CoveredDaysThrough(today, freezes.ToList()));
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetCoveredDaysAsync(
        IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken = default)
    {
        if (memberIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var today = clock.Today;
        var rows = await Memberships
            .Where(m => memberIds.Contains(m.MemberId) && m.Status != MembershipStatus.Cancelled)
            .ToListAsync(cancellationToken);
        var freezes = (await LoadFreezesAsync(rows.Select(r => r.Id), cancellationToken)).ToList();
        return rows.GroupBy(m => m.MemberId).ToDictionary(g => g.Key, g => g.Sum(m => m.CoveredDaysThrough(today, freezes)));
    }

    public async Task<IReadOnlyCollection<Guid>> GetMemberIdsWithPaidMembershipAsync(
        IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken = default)
    {
        if (memberIds.Count == 0)
        {
            return [];
        }

        return await Memberships
            .Where(m => memberIds.Contains(m.MemberId) && m.Status != MembershipStatus.Cancelled && m.AgreedPrice > 0)
            .Select(m => m.MemberId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }
}
