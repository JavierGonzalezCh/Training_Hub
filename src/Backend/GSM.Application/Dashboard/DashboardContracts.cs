namespace GSM.Application.Dashboard;

public sealed record DashboardResponse(
    int ActiveMembers,
    int CheckInsToday,
    decimal RevenueThisMonth,
    decimal RevenueLastMonth,
    int ExpiredMemberships,
    int ExpiringSoon,
    IReadOnlyList<ExpiringMemberRow> ExpiringMemberships);

public sealed record ExpiringMemberRow(
    Guid MembershipId,
    Guid MemberId,
    string MemberName,
    string PlanName,
    DateOnly EndDate,
    int DaysLeft);
