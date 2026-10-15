using GSM.Application.Attendance.Public;
using GSM.Application.Common.Abstractions;
using GSM.Application.Members.Public;
using GSM.Application.Memberships.Public;
using GSM.Application.Payments.Public;
using GSM.Domain.Identity;

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
    public async Task<DashboardResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var today = clock.Today;
        var lastMonth = today.AddMonths(-1);
        var canViewFinances = currentUser.HasPermission(StaffPermission.ViewFinances);

        var settings = await memberships.GetSettingsAsync(gymId, cancellationToken);
        var expiring = await memberships.GetExpiringAsync(gymId, settings.ExpiringDays, cancellationToken);

        // Solo miembros activos: los dados de baja conservan sus membresías pero no cuentan aquí.
        var activeIds = await members.GetActiveMemberIdsAsync(gymId, cancellationToken);
        var counts = await memberships.GetStatusCountsAsync(gymId, activeIds, cancellationToken);

        // Sin el nombre, la tabla no sirve para lo que existe: que recepción llame a renovar.
        var rows = new List<ExpiringMemberRow>(expiring.Count);
        foreach (var e in expiring)
        {
            var member = await members.GetAsync(e.MemberId, cancellationToken);
            var name = member is null ? "(miembro no encontrado)" : $"{member.FirstName} {member.LastName}";
            rows.Add(new ExpiringMemberRow(e.MembershipId, e.MemberId, name, e.PlanName, e.EndDate, e.DaysLeft));
        }

        return new DashboardResponse(
            ActiveMembers: await members.GetActiveCountAsync(gymId, cancellationToken),
            CheckInsToday: await attendance.GetTodayCountAsync(gymId, cancellationToken),
            RevenueThisMonth: !canViewFinances ? null : await payments.GetMonthlyRevenueAsync(gymId, today.Year, today.Month, cancellationToken),
            // B2: el mes en curso va por el día de hoy, así que se compara con los mismos días del mes anterior
            // (del 1 al mismo día; si el mes anterior es más corto, hasta su último día), no con el mes completo.
            RevenueLastMonth: !canViewFinances ? null : await payments.GetRevenueBetweenDaysAsync(
                gymId,
                new DateOnly(lastMonth.Year, lastMonth.Month, 1),
                new DateOnly(lastMonth.Year, lastMonth.Month, Math.Min(today.Day, DateTime.DaysInMonth(lastMonth.Year, lastMonth.Month))),
                cancellationToken),
            ExpiredMemberships: await memberships.GetExpiredCountAsync(gymId, cancellationToken),
            ExpiringSoon: expiring.Count,
            ExpiringMemberships: rows,
            CommercialStatus: new CommercialStatusCounts(
                counts[CommercialStatus.Current],
                counts[CommercialStatus.Expiring],
                counts[CommercialStatus.Grace],
                counts[CommercialStatus.Expired],
                counts[CommercialStatus.Frozen],
                counts[CommercialStatus.NoPlan],
                counts[CommercialStatus.Scheduled]),
            ExpiringDays: settings.ExpiringDays,
            BirthdaysToday: (await members.GetBirthdaysOnAsync(gymId, today, cancellationToken))
                .Select(b => new BirthdayTodayRow(b.Id, b.FullName))
                .ToList());
    }
}
