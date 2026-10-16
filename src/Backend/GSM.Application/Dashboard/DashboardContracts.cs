using GSM.Application.Attendance.Contracts;

namespace GSM.Application.Dashboard;

/// <summary>
/// Panel del gym (Dashboards §1, D1): un objeto con una propiedad por bloque. Un bloque que quien entra no puede ver
/// no viaja (<c>null</c>, no 403), y uno cuyo lector falla también vuelve <c>null</c> con el error registrado: el
/// resto del panel se muestra. Los bloques que sí aplican pero están vacíos viajan con listas vacías, no nulos.
/// </summary>
public sealed record DashboardResponse(
    IndicatorsBlock? Indicators,
    FinancesBlock? Finances,
    AttentionBlock? Attention,
    TodayBlock? Today,
    PeakHoursBlock? PeakHours,
    ExpiringBlock? Expiring,
    MyMembersBlock? MyMembers);

/// <summary>Miembros activos, entradas de hoy, ocupación y estado comercial. Para todos.</summary>
/// <param name="Occupancy">Personas adentro y % del aforo; nulo si esa lectura falló (el resto del bloque sigue).</param>
public sealed record IndicatorsBlock(
    int ActiveMembers,
    int CheckInsToday,
    OccupancyResponse? Occupancy,
    CommercialStatusCounts CommercialStatus,
    int ExpiringDays);

/// <remarks>
/// Solo para el Owner o quien tiene <c>ViewFinances</c>. <c>RevenueLastMonth</c> son los <b>mismos días</b> del mes
/// anterior (del 1 al día de hoy), no el mes completo (B2).
/// </remarks>
public sealed record FinancesBlock(decimal RevenueThisMonth, decimal RevenueLastMonth);

/// <summary>
/// Requiere atención. Cada parte es independiente: <c>Equipment</c> y <c>Rejected</c> para todos; <c>VoidRequests</c>
/// para el Owner y <c>VoidPayments</c>; <c>Cash</c> para el Owner y <c>ViewFinances</c> (cualquier caja abierta) y, para
/// el resto, solo si la caja abierta es la suya.
/// </summary>
public sealed record AttentionBlock(
    EquipmentAttentionBlock? Equipment,
    RejectedTodayBlock? Rejected,
    VoidRequestsBlock? VoidRequests,
    CashBlock? Cash);

public sealed record EquipmentAttentionBlock(
    int OutOfService,
    int InMaintenance,
    int OpenIssues,
    int UrgentIssues,
    IReadOnlyList<OutOfServiceMachineRow> Machines);

public sealed record OutOfServiceMachineRow(Guid Id, string Name, string? Zone, DateOnly? ExpectedBackOn, bool HasUnsafeIssue);

public sealed record RejectedTodayBlock(int Total, int Unseen, IReadOnlyList<RejectedAttemptRow> Items);

/// <param name="Reason">Motivo en snake_case (<c>expired</c>, <c>no_membership</c>…), como en la pantalla de intentos.</param>
public sealed record RejectedAttemptRow(
    Guid MemberId, string MemberName, DateTimeOffset AttemptedAt, int AttemptCount, string Reason, bool Seen);

public sealed record VoidRequestsBlock(int Pending);

/// <param name="IsStale">Abierta hace más de 16 horas: caja olvidada.</param>
/// <param name="IsMine">La abrió quien consulta.</param>
public sealed record CashBlock(
    Guid ShiftId, string? OpenedByName, DateTimeOffset OpenedAt, decimal ExpectedCash, bool IsStale, bool IsMine);

/// <summary>
/// Hoy. Cada lista es independiente: <c>Classes</c> es nula si el gym apagó las clases o su lector falló.
/// </summary>
public sealed record TodayBlock(
    IReadOnlyList<TodayClassRow>? Classes,
    IReadOnlyList<BirthdayTodayRow>? Birthdays,
    IReadOnlyList<RecentEntryRow>? RecentEntries);

/// <param name="Capacity">Cupo efectivo; nulo = entrada libre.</param>
public sealed record TodayClassRow(
    Guid ClassId,
    string ClassName,
    TimeOnly StartTime,
    int DurationMinutes,
    string? InstructorName,
    bool Cancelled,
    int? Capacity,
    int Booked,
    int Waiting);

public sealed record RecentEntryRow(Guid MemberId, string MemberName, DateTimeOffset CheckedInAt, string Method);

/// <summary>Miembro activo que cumple años hoy (el staff lo saluda en persona, Comunicaciones §F).</summary>
public sealed record BirthdayTodayRow(Guid MemberId, string MemberName);

/// <summary>Afluencia por hora de los últimos <c>WindowDays</c> días. Solo las horas con entradas.</summary>
public sealed record PeakHoursBlock(int WindowDays, IReadOnlyList<HourBucket> Buckets);

public sealed record HourBucket(int Hour, int Count);

/// <summary>
/// Miembros activos por estado comercial hoy ("32 al día · 5 por vencer · 2 en gracia · 7 vencidos").
/// El mismo `expiring_days` del gym define "por vencer" aquí, en el letrero y en la lista.
/// </summary>
public sealed record CommercialStatusCounts(
    int Current, int Expiring, int Grace, int Expired, int Frozen, int NoPlan, int Scheduled);

/// <summary>
/// Por vencer y en gracia (lista corta, la más urgente primero) con los totales. <c>DaysLeft</c> negativo = ya venció
/// y sigue en gracia. "Ver todos" lleva a la pantalla de membresías por vencer.
/// </summary>
/// <param name="ExpiringCount">Membresías que vencen dentro de <c>ExpiringDays</c>.</param>
/// <param name="ExpiredCount">Membresías vencidas y no renovadas.</param>
public sealed record ExpiringBlock(
    int ExpiringCount,
    int ExpiredCount,
    int ExpiringDays,
    IReadOnlyList<ExpiringMemberRow> Items);

public sealed record ExpiringMemberRow(
    Guid MembershipId,
    Guid MemberId,
    string MemberName,
    string PlanName,
    DateOnly EndDate,
    int DaysLeft);

/// <summary>Para el entrenador (<c>Trainer</c>): lo suyo de hoy y sus miembros.</summary>
/// <param name="MembersWithoutRoutine">Cuántos de sus miembros activos no tienen rutina activa.</param>
/// <param name="WithoutRoutine">Los primeros de esa lista, para ir directo a ellos.</param>
public sealed record MyMembersBlock(
    IReadOnlyList<TodayClassRow> MyClassesToday,
    int RoutinesDue,
    int RoutinesOverdue,
    int MembersWithoutRoutine,
    IReadOnlyList<MemberNameRow> WithoutRoutine);

public sealed record MemberNameRow(Guid MemberId, string MemberName);
