using GSM.Application.Attendance.Public;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Logs;
using GSM.Application.Gyms.Public;
using GSM.Application.Identity.Public;
using GSM.Application.Members.Public;
using GSM.Application.Memberships.Public;
using GSM.Application.Payments.Public;
using GSM.Application.SuperAdmin.Abstractions;
using GSM.Application.SuperAdmin.Contracts;
using GSM.Domain.SuperAdmin;

namespace GSM.Application.SuperAdmin;

/// <summary>
/// Panel de SuperAdmin: "cómo va el negocio". Un endpoint, un response, compuesto de los
/// <c>Public/I{Módulo}Reader</c> — el mismo patrón que el dashboard de la etapa 8. El superadmin
/// no tiene <c>gym_id</c>, así que el Global Query Filter ya está desactivado y estas agregaciones
/// ven todo. Todo se agrupa por <b>día de negocio</b> (<see cref="IClock.BusinessDayRange"/>).
/// </summary>
/// <remarks>
/// Los bloques nuevos de D3 (<c>Attention</c>, <c>Startup</c>, <c>Limits</c>) son anulables: si su lectura falla
/// quedan en <c>null</c>, con el error en <c>logs.errors</c>, y el resto del panel llega (Dashboards §4). La
/// evolución de 12 meses no viaja aquí: es <c>GET /api/admin/metrics/monthly</c> (S7), que el panel pide aparte.
/// </remarks>
public sealed class GetAdminDashboardHandler(
    IClock clock,
    IGymSubscriptionRepository subscriptions,
    ISubscriptionPaymentRepository payments,
    ISaasPlanRepository saasPlans,
    IMemberReader members,
    IAttendanceReader attendance,
    IPaymentReader memberPayments,
    IMembershipReader memberships,
    IGymReader gyms,
    IIdentityReader identity,
    ILogStore logStore)
{
    private const int ExpiringWindowDays = 15;

    /// <summary>Filas máximas de cada lista del panel; el total real viaja aparte para el "Ver todos".</summary>
    public const int ListLimit = 10;

    /// <summary>Días sin entradas a partir de los cuales un gym se marca inactivo (Dashboards §3).</summary>
    public const int InactivityDays = 7;

    /// <summary>Porcentaje del límite de miembros desde el que un gym aparece en "Límites".</summary>
    public const int NearLimitPercent = 90;

    /// <summary>Días para inhabilitarse a partir de los cuales un gym cuenta como "por inhabilitarse" (SuperAdmin §V3).</summary>
    public const int AboutToDisableDays = 3;

    public async Task<AdminDashboardResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var firstOfThisMonth = new DateOnly(today.Year, today.Month, 1);
        var firstOfNextMonth = firstOfThisMonth.AddMonths(1);
        var firstOfLastMonth = firstOfThisMonth.AddMonths(-1);

        var thisMonthStart = clock.BusinessDayRange(firstOfThisMonth).Start;
        var nextMonthStart = clock.BusinessDayRange(firstOfNextMonth).Start;
        var lastMonthStart = clock.BusinessDayRange(firstOfLastMonth).Start;

        var rows = await subscriptions.GetPortfolioAsync(cancellationToken);

        // Cuatro consultas agrupadas por gym para toda la cartera, no cuatro por cada gym (N+1).
        var activeByGym = await members.GetActiveCountsByGymAsync(cancellationToken);
        var checkInsByGym = await attendance.GetMonthlyCountsByGymAsync(today.Year, today.Month, cancellationToken);
        var revenueByGym = await memberPayments.GetMonthlyRevenueByGymAsync(today.Year, today.Month, cancellationToken);
        var lastCheckInByGym = await attendance.GetLastAttendanceDateByGymAsync(cancellationToken);

        // Límite efectivo y miembros que cuentan, por gym. Lo comparten el uso por gym y el bloque "Límites"; si
        // falla, ambos quedan sin ese dato y el resto del panel sigue.
        Dictionary<Guid, (int? Limit, int Counted)>? limits = null;
        try
        {
            limits = await LoadLimitsAsync(rows, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RecordFailure("limits", ex);
        }

        var countByStatus = new Dictionary<string, int>();
        var contracted = 0m;
        var mrr = 0m;
        var expiring = 0;

        var usage = new List<GymUsageRow>(rows.Count);
        var effectiveByGym = new Dictionary<Guid, EffectiveSubscriptionStatus>(rows.Count);
        var totalActiveMembers = 0;
        var totalCheckIns = 0;

        foreach (var row in rows)
        {
            var effective = GymSubscription.DeriveStatus(
                row.ManualStatus, today, row.PaidThrough, row.TrialEndsOn, row.TrialGraceDays);
            effectiveByGym[row.GymId] = effective;
            var key = effective.ToString();
            countByStatus[key] = countByStatus.GetValueOrDefault(key) + 1;

            var live = effective is not (EffectiveSubscriptionStatus.Suspended or EffectiveSubscriptionStatus.Disabled);
            if (live)
            {
                contracted += row.MonthlyPrice;
            }

            // Mismo criterio que la foto mensual (V4): al día o en mora.
            if (effective is EffectiveSubscriptionStatus.Active or EffectiveSubscriptionStatus.Overdue)
            {
                mrr += row.MonthlyPrice;
            }

            if (effective == EffectiveSubscriptionStatus.Active
                && row.PaidThrough <= today.AddDays(ExpiringWindowDays))
            {
                expiring++;
            }

            var activeMembers = activeByGym.GetValueOrDefault(row.GymId);
            var checkIns = checkInsByGym.GetValueOrDefault(row.GymId);
            var memberRevenue = revenueByGym.GetValueOrDefault(row.GymId);
            DateOnly? lastCheckIn = lastCheckInByGym.TryGetValue(row.GymId, out var last) ? last : null;
            int? daysSince = lastCheckIn is { } l ? today.DayNumber - l.DayNumber : null;

            // Inactivo: un gym vivo, con al menos una semana de vida, sin entradas en los últimos 7 días (o nunca).
            // Uno recién creado o uno ya inhabilitado/suspendido no es una alerta de uso: es otra historia.
            var inactive = live
                && row.StartedOn <= today.AddDays(-InactivityDays)
                && (daysSince is null || daysSince >= InactivityDays);

            int? counted = null;
            int? limit = null;
            if (limits is not null && limits.TryGetValue(row.GymId, out var found))
            {
                counted = found.Counted;
                limit = found.Limit;
            }

            usage.Add(new GymUsageRow(
                row.GymId, row.GymName, key, activeMembers, checkIns, memberRevenue, lastCheckIn,
                counted, limit, inactive, daysSince));

            totalActiveMembers += activeMembers;
            totalCheckIns += checkIns;
        }

        var collectedThisMonth = await payments.SumInRangeAsync(thisMonthStart, nextMonthStart, cancellationToken);
        var collectedLastMonth = await payments.SumInRangeAsync(lastMonthStart, thisMonthStart, cancellationToken);
        var pending = Math.Max(0m, contracted - collectedThisMonth);

        var attention = BuildAttention(rows, effectiveByGym, today);
        var limitsBlock = limits is null ? null : BuildLimits(rows, effectiveByGym, limits);
        var startup = await SafeAsync("startup", () => BuildStartupAsync(rows, effectiveByGym, today, cancellationToken));

        return new AdminDashboardResponse(
            new PortfolioSummary(countByStatus, expiring),
            new PlatformRevenue(contracted, collectedThisMonth, collectedLastMonth, mrr, pending),
            usage.OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            new PlatformTotals(rows.Count, totalActiveMembers, totalCheckIns),
            attention,
            startup,
            limitsBlock);
    }

    /// <summary>
    /// Límite efectivo por gym con el mismo criterio que la cartera: el override del cliente, o el del plan elegido,
    /// o ninguno. Nulo = sin límite.
    /// </summary>
    private async Task<Dictionary<Guid, (int? Limit, int Counted)>> LoadLimitsAsync(
        IReadOnlyList<PortfolioRow> rows, CancellationToken cancellationToken)
    {
        var planList = await saasPlans.ListAsync(cancellationToken);
        var plansById = planList.ToDictionary(p => p.Id);
        var result = new Dictionary<Guid, (int? Limit, int Counted)>(rows.Count);

        foreach (var row in rows)
        {
            var plan = row.SaasPlanId is { } planId
                ? plansById.GetValueOrDefault(planId)
                : null;
            var counted = await memberships.GetCountedMemberCountAsync(row.GymId, cancellationToken);
            result[row.GymId] = (row.MemberLimitOverride ?? plan?.MemberLimit, counted);
        }

        return result;
    }

    private AdminAttentionBlock? BuildAttention(
        IReadOnlyList<PortfolioRow> rows, Dictionary<Guid, EffectiveSubscriptionStatus> effectiveByGym, DateOnly today)
    {
        try
        {
            int? DaysToDisable(PortfolioRow r) => GymSubscription.DaysUntilDisabled(
                r.ManualStatus, today, r.PaidThrough, r.TrialEndsOn, r.TrialGraceDays);

            AttentionGymRow Row(PortfolioRow r, bool withExpiry) => new(
                r.GymId, r.GymName, effectiveByGym[r.GymId].ToString(), r.PaidThrough,
                withExpiry ? null : DaysToDisable(r),
                withExpiry ? r.PaidThrough.DayNumber - today.DayNumber : null);

            AttentionList ToList(IEnumerable<PortfolioRow> source, bool withExpiry, Func<PortfolioRow, int> order)
            {
                var all = source.OrderBy(order).ThenBy(r => r.GymName, StringComparer.OrdinalIgnoreCase).ToList();
                return new AttentionList(all.Count, all.Take(ListLimit).Select(r => Row(r, withExpiry)).ToList());
            }

            int ByDaysToDisable(PortfolioRow r) => DaysToDisable(r) ?? int.MaxValue;

            var trialEnded = rows.Where(r => effectiveByGym[r.GymId] == EffectiveSubscriptionStatus.TrialEnded);
            var overdue = rows.Where(r => effectiveByGym[r.GymId] == EffectiveSubscriptionStatus.Overdue);
            var aboutToDisable = rows.Where(r =>
                effectiveByGym[r.GymId] is EffectiveSubscriptionStatus.TrialEnded or EffectiveSubscriptionStatus.Overdue
                && ByDaysToDisable(r) <= AboutToDisableDays);
            var expiring = rows.Where(r =>
                effectiveByGym[r.GymId] == EffectiveSubscriptionStatus.Active
                && r.PaidThrough <= today.AddDays(ExpiringWindowDays));

            return new AdminAttentionBlock(
                ToList(trialEnded, false, ByDaysToDisable),
                ToList(overdue, false, ByDaysToDisable),
                ToList(aboutToDisable, false, ByDaysToDisable),
                ToList(expiring, true, r => r.PaidThrough.DayNumber));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RecordFailure("attention", ex);
            return null;
        }
    }

    private static AdminLimitsBlock BuildLimits(
        IReadOnlyList<PortfolioRow> rows,
        Dictionary<Guid, EffectiveSubscriptionStatus> effectiveByGym,
        Dictionary<Guid, (int? Limit, int Counted)> limits)
    {
        var near = new List<LimitGymRow>();
        foreach (var row in rows)
        {
            if (effectiveByGym[row.GymId] == EffectiveSubscriptionStatus.Suspended
                || !limits.TryGetValue(row.GymId, out var entry)
                || entry.Limit is not { } limit
                || entry.Counted * 100 < limit * NearLimitPercent)
            {
                continue;
            }

            near.Add(new LimitGymRow(row.GymId, row.GymName, entry.Counted, limit, entry.Counted * 100 / limit));
        }

        var ordered = near
            .OrderByDescending(r => r.UsagePercent)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new AdminLimitsBlock(ordered.Count, ordered.Take(ListLimit).ToList());
    }

    private async Task<AdminStartupBlock> BuildStartupAsync(
        IReadOnlyList<PortfolioRow> rows,
        Dictionary<Guid, EffectiveSubscriptionStatus> effectiveByGym,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var states = (await gyms.ListStartupStatesAsync(cancellationToken)).ToDictionary(s => s.GymId);
        var pendingOwners = (await identity.ListOwnersPendingActivationAsync(cancellationToken))
            .GroupBy(o => o.GymId)
            .ToDictionary(g => g.Key, g => g.First());

        var items = new List<StartupGymRow>();
        foreach (var row in rows)
        {
            // Un gym suspendido (o ya inhabilitado) no está "arrancando": es otro caso del panel.
            if (effectiveByGym[row.GymId] is EffectiveSubscriptionStatus.Suspended or EffectiveSubscriptionStatus.Disabled
                || !states.TryGetValue(row.GymId, out var state))
            {
                continue;
            }

            var ownerPending = pendingOwners.GetValueOrDefault(row.GymId);
            var onboardingPending = !state.OnboardingDone;
            if (ownerPending is null && !onboardingPending)
            {
                continue;
            }

            var created = DateOnly.FromDateTime(clock.ToBusinessLocal(state.CreatedAt));
            items.Add(new StartupGymRow(
                row.GymId, row.GymName, Math.Max(0, today.DayNumber - created.DayNumber),
                ownerPending is not null, onboardingPending,
                ownerPending?.UserId, ownerPending?.Email));
        }

        var ordered = items
            .OrderByDescending(i => i.DaysSinceCreated)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new AdminStartupBlock(ordered.Count, ordered.Take(ListLimit).ToList());
    }

    private async Task<T?> SafeAsync<T>(string block, Func<Task<T>> compute) where T : class
    {
        try
        {
            return await compute();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RecordFailure(block, ex);
            return null;
        }
    }

    private void RecordFailure(string block, Exception exception)
        => logStore.RecordError(new ErrorLogEntry(
            ErrorSource.Api, exception, Method: "GET", Path: "/api/admin/dashboard", Endpoint: $"admin-dashboard:{block}"));
}
