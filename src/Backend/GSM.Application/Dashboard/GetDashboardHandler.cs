using GSM.Application.Attendance.Public;
using GSM.Application.Common.Abstractions;
using GSM.Application.Members.Public;
using GSM.Application.Memberships.Public;
using GSM.Application.Payments.Public;

namespace GSM.Application.Dashboard;

/// <summary>
/// Un endpoint, un response. Se compone de los <c>Public/I{Módulo}Reader</c>; sin queries cruzadas.
/// </summary>
public sealed class GetDashboardHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberReader members,
    IAttendanceReader attendance,
    IPaymentReader payments,
    IMembershipReader memberships)
{
    private const int ExpiringWindowDays = 7;

    public async Task<DashboardResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var today = clock.Today;
        var lastMonth = today.AddMonths(-1);

        var expiring = await memberships.GetExpiringAsync(gymId, ExpiringWindowDays, cancellationToken);

        return new DashboardResponse(
            ActiveMembers: await members.GetActiveCountAsync(gymId, cancellationToken),
            CheckInsToday: await attendance.GetTodayCountAsync(gymId, cancellationToken),
            RevenueThisMonth: await payments.GetMonthlyRevenueAsync(gymId, today.Year, today.Month, cancellationToken),
            RevenueLastMonth: await payments.GetMonthlyRevenueAsync(gymId, lastMonth.Year, lastMonth.Month, cancellationToken),
            ExpiredMemberships: await memberships.GetExpiredCountAsync(gymId, cancellationToken),
            ExpiringSoon: expiring.Count,
            ExpiringMemberships: expiring
                .Select(e => new ExpiringMemberRow(e.MembershipId, e.MemberId, e.PlanName, e.EndDate, e.DaysLeft))
                .ToList());
    }
}
