using GSM.Application.Common.Abstractions;
using GSM.Application.Members.Public;
using GSM.Application.Memberships;
using GSM.Application.Memberships.Public;
using GSM.Domain.Memberships;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Memberships;

/// <summary>Lectura para otros módulos. Respeta el Global Query Filter (datos de tenant).</summary>
public sealed class MembershipReader(GsmDbContext dbContext, IClock clock, IMemberReader members) : IMembershipReader
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
            .Select(m => new SpanRow(m.Id, m.GymId, m.MemberId, m.Status, m.StartDate, m.EndDate, m.EntriesTotal))
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return CommercialStatusCalculator.Compute([], date, 0, 0);
        }

        var freezes = await LoadFreezesAsync(rows.Select(r => r.Id), cancellationToken);
        var used = await LoadUsedCountsAsync(rows, cancellationToken);
        var settings = await GetSettingsAsync(rows[0].GymId, cancellationToken);
        return CommercialStatusCalculator.Compute(
            rows.Select(r => SpanOf(r, freezes, used)),
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
            .Select(m => new SpanRow(m.Id, m.GymId, m.MemberId, m.Status, m.StartDate, m.EndDate, m.EntriesTotal))
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
            .Select(m => new SpanRow(m.Id, m.GymId, m.MemberId, m.Status, m.StartDate, m.EndDate, m.EntriesTotal))
            .ToListAsync(cancellationToken);

        var computed = await ComputeAsync(rows, cancellationToken);
        return computed.Where(kv => statuses.Contains(kv.Value)).Select(kv => kv.Key).ToList();
    }

    public async Task<IReadOnlyDictionary<CommercialStatus, int>> GetStatusCountsAsync(
        Guid gymId, IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken = default)
    {
        var rows = await Memberships
            .Where(m => m.GymId == gymId && m.Status != MembershipStatus.Cancelled)
            .Select(m => new SpanRow(m.Id, m.GymId, m.MemberId, m.Status, m.StartDate, m.EndDate, m.EntriesTotal))
            .ToListAsync(cancellationToken);

        var computed = await ComputeAsync(rows, cancellationToken);
        var counts = Enum.GetValues<CommercialStatus>().ToDictionary(s => s, _ => 0);
        foreach (var id in memberIds.Distinct())
        {
            counts[computed.GetValueOrDefault(id, CommercialStatus.NoPlan)]++;
        }

        return counts;
    }

    public Task<int> GetActivePlanCountAsync(Guid gymId, CancellationToken cancellationToken = default)
        => Plans.CountAsync(p => p.GymId == gymId && p.IsActive, cancellationToken);

    public async Task<int> GetCountedMemberCountAsync(Guid gymId, CancellationToken cancellationToken = default)
    {
        var rows = await Memberships
            .Where(m => m.GymId == gymId && m.Status != MembershipStatus.Cancelled)
            .Select(m => new SpanRow(m.Id, m.GymId, m.MemberId, m.Status, m.StartDate, m.EndDate, m.EntriesTotal))
            .ToListAsync(cancellationToken);

        var computed = await ComputeAsync(rows, cancellationToken);

        // S2-3: un miembro desactivado no cuenta para el límite aunque conserve una membresía vigente.
        var active = (await members.GetActiveMemberIdsAsync(gymId, cancellationToken)).ToHashSet();
        return computed.Count(kv => kv.Value.CountsTowardMemberLimit() && active.Contains(kv.Key));
    }

    private static IReadOnlyList<FreezeSpan> FreezesOf(IReadOnlyList<MembershipFreeze> freezes, Guid membershipId)
        => freezes.Where(f => f.MembershipId == membershipId).Select(f => new FreezeSpan(f.StartsOn, f.EndsOn)).ToList();

    private sealed record SpanRow(
        Guid Id, Guid GymId, Guid MemberId, MembershipStatus Status, DateOnly Start, DateOnly End, int? EntriesTotal);

    /// <summary>Entradas consumidas de cada tiquetera pedida (las que no tienen consumo no aparecen).</summary>
    private async Task<Dictionary<Guid, int>> LoadUsedCountsAsync(
        IEnumerable<SpanRow> rows, CancellationToken cancellationToken)
    {
        var ids = rows.Where(r => r.EntriesTotal != null).Select(r => r.Id).Distinct().ToList();
        return ids.Count == 0
            ? []
            : await dbContext.Set<PassUsage>()
                .Where(u => ids.Contains(u.MembershipId))
                .GroupBy(u => u.MembershipId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
    }

    private static MembershipSpan SpanOf(
        SpanRow r, IReadOnlyList<MembershipFreeze> freezes, IReadOnlyDictionary<Guid, int> used)
        => new(r.Status, r.Start, r.End, FreezesOf(freezes, r.Id), r.EntriesTotal, used.GetValueOrDefault(r.Id));

    private async Task<Dictionary<Guid, CommercialStatus>> ComputeAsync(
        List<SpanRow> rows, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var freezes = await LoadFreezesAsync(rows.Select(r => r.Id), cancellationToken);
        var used = await LoadUsedCountsAsync(rows, cancellationToken);
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
                member.Select(r => SpanOf(r, freezes, used)),
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

        var usages = dbContext.Set<PassUsage>();

        // MS5-4: además de las que vencen por fecha, las tiqueteras vigentes con pocas entradas restantes.
        return await NotFrozenOn(today)
            .Where(m => m.GymId == gymId
                && m.Status == MembershipStatus.Active
                && m.EndDate >= today
                && (m.EndDate <= limit
                    || (m.EntriesTotal != null
                        && m.EntriesTotal.Value - usages.Count(u => u.MembershipId == m.Id) <= CommercialStatusCalculator.PassLowEntries)))
            .OrderBy(m => m.EndDate)
            .Join(Plans, m => m.PlanId, p => p.Id,
                (m, p) => new { m.Id, m.MemberId, p.Name, m.EndDate })
            .Select(x => new ExpiringMembershipView(
                x.Id, x.MemberId, x.Name, x.EndDate, x.EndDate.DayNumber - today.DayNumber))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChargeableMembershipView>> GetChargeableAsync(
        Guid gymId, CancellationToken cancellationToken = default)
    {
        var today = clock.Today;
        return await Memberships
            .Where(m => m.GymId == gymId
                && m.Status == MembershipStatus.Active
                && m.EndDate >= today
                && m.AgreedPrice + m.EnrollmentFee - m.CreditApplied > 0)
            .Join(Plans, m => m.PlanId, p => p.Id,
                (m, p) => new ChargeableMembershipView(
                    m.Id, m.MemberId, p.Name, m.AgreedPrice + m.EnrollmentFee - m.CreditApplied, m.StartDate))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReminderMembershipView>> GetReminderCandidatesAsync(
        Guid gymId, int daysBefore, CancellationToken cancellationToken = default)
    {
        var today = clock.Today;
        var graceDays = (await GetSettingsAsync(gymId, cancellationToken)).GraceDays;
        var from = today.AddDays(-graceDays);
        var to = today.AddDays(daysBefore);

        // Las tiqueteras no tienen gracia (MS5, CO7): un vencimiento ya pasado no es candidato y no se avisa
        // el "último día de gracia" (su GraceDays es 0).
        var rows = await NotFrozenOn(today)
            .Where(m => m.GymId == gymId
                && m.Status == MembershipStatus.Active
                && m.EndDate <= to
                && (m.EntriesTotal == null ? m.EndDate >= from : m.EndDate >= today))
            .OrderBy(m => m.EndDate)
            .Join(Plans, m => m.PlanId, p => p.Id,
                (m, p) => new { m.Id, m.MemberId, p.Name, m.EndDate, m.EntriesTotal })
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => new ReminderMembershipView(
                x.Id, x.MemberId, x.Name, x.EndDate, x.EndDate.DayNumber - today.DayNumber,
                x.EntriesTotal is null ? graceDays : 0))
            .ToList();
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
        var usages = dbContext.Set<PassUsage>();
        return NotFrozenOn(today).CountAsync(
            m => m.GymId == gymId
                && m.Status == MembershipStatus.Active
                && m.EndDate >= today
                && (m.EndDate <= limit
                    || (m.EntriesTotal != null
                        && m.EntriesTotal.Value - usages.Count(u => u.MembershipId == m.Id) <= CommercialStatusCalculator.PassLowEntries)),
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
