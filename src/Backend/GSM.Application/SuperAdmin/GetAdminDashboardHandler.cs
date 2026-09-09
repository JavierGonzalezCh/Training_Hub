using GSM.Application.Attendance.Public;
using GSM.Application.Common.Abstractions;
using GSM.Application.Members.Public;
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
public sealed class GetAdminDashboardHandler(
    IClock clock,
    IGymSubscriptionRepository subscriptions,
    ISubscriptionPaymentRepository payments,
    IMemberReader members,
    IAttendanceReader attendance,
    IPaymentReader memberPayments)
{
    private const int ExpiringWindowDays = 15;

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

        var countByStatus = new Dictionary<string, int>();
        var contracted = 0m;
        var expiring = 0;

        var usage = new List<GymUsageRow>(rows.Count);
        var totalActiveMembers = 0;
        var totalCheckIns = 0;

        foreach (var row in rows)
        {
            var effective = GymSubscription.DeriveStatus(
                row.ManualStatus, today, row.PaidThrough, row.TrialEndsOn);
            var key = effective.ToString();
            countByStatus[key] = countByStatus.GetValueOrDefault(key) + 1;

            var live = effective is not (EffectiveSubscriptionStatus.Suspended or EffectiveSubscriptionStatus.Disabled);
            if (live)
            {
                contracted += row.MonthlyPrice;
            }

            if (effective == EffectiveSubscriptionStatus.Active
                && row.PaidThrough <= today.AddDays(ExpiringWindowDays))
            {
                expiring++;
            }

            var activeMembers = await members.GetActiveCountAsync(row.GymId, cancellationToken);
            var checkIns = await attendance.GetMonthlyCountAsync(row.GymId, today.Year, today.Month, cancellationToken);
            var memberRevenue = await memberPayments.GetMonthlyRevenueAsync(
                row.GymId, today.Year, today.Month, cancellationToken);
            var lastCheckIn = await attendance.GetLastAttendanceDateAsync(row.GymId, cancellationToken);

            usage.Add(new GymUsageRow(
                row.GymId, row.GymName, key, activeMembers, checkIns, memberRevenue, lastCheckIn));

            totalActiveMembers += activeMembers;
            totalCheckIns += checkIns;
        }

        var collectedThisMonth = await payments.SumInRangeAsync(thisMonthStart, nextMonthStart, cancellationToken);
        var collectedLastMonth = await payments.SumInRangeAsync(lastMonthStart, thisMonthStart, cancellationToken);

        return new AdminDashboardResponse(
            new PortfolioSummary(countByStatus, expiring),
            new PlatformRevenue(contracted, collectedThisMonth, collectedLastMonth),
            usage.OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            new PlatformTotals(rows.Count, totalActiveMembers, totalCheckIns));
    }
}
