namespace GSM.Application.Dashboard;

/// <remarks>
/// Los ingresos solo salen para el Owner o quien tiene <c>ViewFinances</c>; para el resto son nulos.
/// <c>RevenueLastMonth</c> son los <b>mismos días</b> del mes anterior (del 1 al día de hoy), no el mes completo (B2).
/// </remarks>
public sealed record DashboardResponse(
    int ActiveMembers,
    int CheckInsToday,
    decimal? RevenueThisMonth,
    decimal? RevenueLastMonth,
    int ExpiredMemberships,
    int ExpiringSoon,
    IReadOnlyList<ExpiringMemberRow> ExpiringMemberships,
    CommercialStatusCounts CommercialStatus,
    int ExpiringDays);

/// <summary>
/// Miembros activos por estado comercial hoy ("32 al día · 5 por vencer · 2 en gracia · 7 vencidos").
/// El mismo `expiring_days` del gym define "por vencer" aquí, en el letrero y en la lista.
/// </summary>
public sealed record CommercialStatusCounts(
    int Current, int Expiring, int Grace, int Expired, int Frozen, int NoPlan);

public sealed record ExpiringMemberRow(
    Guid MembershipId,
    Guid MemberId,
    string MemberName,
    string PlanName,
    DateOnly EndDate,
    int DaysLeft);
