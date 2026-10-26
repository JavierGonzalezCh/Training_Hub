using GSM.Domain.Gyms;
using GSM.Domain.Payments;

namespace GSM.Application.SuperAdmin.Contracts;

/// <summary>Filtros opcionales de la cartera: estado efectivo y tipo de negocio (nombre del enum, case-insensitive).</summary>
public sealed record PortfolioQuery
{
    public string? Status { get; init; }

    public string? Type { get; init; }
}

public sealed record PortfolioItem(
    Guid GymId,
    string Name,
    string Slug,
    string EffectiveStatus,
    string ManualStatus,
    string Plan,
    decimal MonthlyPrice,
    DateOnly PaidThrough,
    int? DaysUntilDisabled,
    int? MemberLimit,
    int CountedMembers,
    string Type = nameof(GymType.Gym));

public sealed record PortfolioResponse(
    IReadOnlyList<PortfolioItem> Items,
    IReadOnlyDictionary<string, int> CountByStatus);

public sealed record GymDetailResponse(
    Guid GymId,
    string Name,
    string Slug,
    string? Nit,
    string? Phone,
    string? Email,
    SubscriptionDetail Subscription,
    string Type = nameof(GymType.Gym),
    GymTemplateStatus? Template = null);

/// <summary>
/// Qué partes de la plantilla del tipo quedaron aplicadas al crear el gym (docs/GSM-tipos-de-negocio.md §7.3). Una
/// marca en <c>false</c> es un handler que falló o un gym anterior a las plantillas: el gym opera con los valores
/// por defecto de ese módulo y el fallo queda en el reintento de eventos.
/// </summary>
public sealed record GymTemplateStatus(bool Checkin, bool Memberships, bool Routines, bool Achievements)
{
    public bool Complete => Checkin && Memberships && Routines && Achievements;
}

/// <param name="Plan">Nombre del plan del SaaS.</param>
/// <param name="MemberLimit">Límite efectivo (override del cliente o el del plan). Nulo = sin límite.</param>
/// <param name="MemberLimitOverride">Límite propio del cliente. Nulo = usa el del plan.</param>
/// <param name="CountedMembers">Miembros que cuentan hoy para el límite (uso: "142 / 150").</param>
public sealed record SubscriptionDetail(
    string Plan,
    Guid? SaasPlanId,
    int? MemberLimit,
    int? MemberLimitOverride,
    int CountedMembers,
    decimal MonthlyPrice,
    DateOnly StartedOn,
    DateOnly? TrialEndsOn,
    DateOnly PaidThrough,
    string EffectiveStatus,
    string ManualStatus,
    int? DaysUntilDisabled,
    string? Notes,
    int TrialGraceDays);

/// <param name="SaasPlanId">Plan del SaaS (S2). Si se omite, el plan actual no cambia.</param>
/// <param name="MemberLimitOverride">Límite propio del cliente; nulo = el del plan (el PUT reemplaza el valor anterior).</param>
/// <param name="TrialGraceDays">Días de <c>trial_ended</c> tras la prueba (0 a 60). Nulo = no cambia el valor actual.</param>
public sealed record UpdateSubscriptionRequest(
    decimal MonthlyPrice,
    Guid? SaasPlanId = null,
    int? MemberLimitOverride = null,
    int? TrialGraceDays = null);

public sealed record SaasPlanItem(
    Guid Id,
    string Name,
    int? MemberLimit,
    decimal SuggestedPrice,
    bool IsActive);

/// <param name="MemberLimit">Nulo = sin límite.</param>
public sealed record CreateSaasPlanRequest(string Name, int? MemberLimit, decimal SuggestedPrice);

public sealed record UpdateSaasPlanRequest(string Name, int? MemberLimit, decimal SuggestedPrice, bool IsActive);

/// <summary>Uso del límite de miembros de un gym ("142 / 150"). <c>Limit</c> nulo = sin límite.</summary>
public sealed record MemberUsageResponse(int Counted, int? Limit, string PlanName);

public sealed record RenewSubscriptionRequest(DateOnly PaidThrough, string Reason);

public sealed record SuspendGymRequest(string Reason);

/// <summary>
/// Registrar un pago del gym al SaaS. <c>CoversFrom</c>/<c>CoversThrough</c> son opcionales: si se
/// omiten, el periodo arranca el día siguiente al <c>paid_through</c> vigente y dura un mes.
/// El usuario que registra sale de <c>ICurrentUserService</c>, nunca del body.
/// </summary>
public sealed record RegisterSubscriptionPaymentRequest(
    decimal Amount,
    PaymentMethod Method,
    DateOnly? CoversFrom,
    DateOnly? CoversThrough,
    string? Reference,
    string? Notes);

public sealed record SubscriptionPaymentItem(
    Guid Id,
    Guid GymId,
    decimal Amount,
    string Method,
    DateOnly CoversFrom,
    DateOnly CoversThrough,
    DateTimeOffset PaidAt,
    Guid RegisteredBy,
    string? Reference,
    string? Notes);

/// <summary>Respuesta del registro: el asiento y la suscripción ya renovada.</summary>
public sealed record RegisterSubscriptionPaymentResponse(
    SubscriptionPaymentItem Payment,
    SubscriptionDetail Subscription);

public sealed record PlatformPaymentItem(
    Guid Id,
    Guid GymId,
    string GymName,
    decimal Amount,
    string Method,
    DateOnly CoversFrom,
    DateOnly CoversThrough,
    DateTimeOffset PaidAt,
    Guid RegisteredBy);

public sealed record PlatformPaymentsResponse(
    IReadOnlyList<PlatformPaymentItem> Items,
    decimal Total,
    DateOnly From,
    DateOnly To);

// --- Panel ---------------------------------------------------------------------------------

public sealed record AdminDashboardResponse(
    PortfolioSummary Portfolio,
    PlatformRevenue Revenue,
    IReadOnlyList<GymUsageRow> GymUsage,
    PlatformTotals Platform,
    AdminAttentionBlock? Attention,
    AdminStartupBlock? Startup,
    AdminLimitsBlock? Limits,
    IReadOnlyList<TypeMetricsRow>? ByType = null);

/// <summary>
/// Cartera partida por tipo de negocio (docs/GSM-tipos-de-negocio.md §11). <c>Paying</c>: al día o en mora; <c>Trial</c>:
/// en prueba o con la prueba terminada. <c>Mrr</c>: lo mensual contratado por los que pagan (mismo criterio que
/// <c>PlatformRevenue.Mrr</c>). <c>ConversionPercent</c>: <c>Paying / (Paying + Trial)</c>, el avance prueba → pago; nulo si
/// ninguno está en esos estados.
/// </summary>
public sealed record TypeMetricsRow(
    string Type,
    int Gyms,
    int Paying,
    int Trial,
    decimal Mrr,
    int ActiveMembers,
    int? ConversionPercent);

/// <summary>
/// "Requiere acción" (Dashboards §3): cuatro listas cortas (máximo 10 filas, con el total real para el "Ver todos").
/// Un gym puede estar en más de una (por ejemplo, en mora y por inhabilitarse).
/// </summary>
public sealed record AdminAttentionBlock(
    AttentionList TrialEnded,
    AttentionList Overdue,
    AttentionList AboutToDisable,
    AttentionList ExpiringSoon);

public sealed record AttentionList(int Total, IReadOnlyList<AttentionGymRow> Items);

/// <param name="DaysUntilDisabled">Solo en prueba terminada y mora; nulo en "vencen pronto".</param>
/// <param name="DaysUntilExpiry">Solo en "vencen pronto": días hasta <c>paid_through</c>.</param>
public sealed record AttentionGymRow(
    Guid GymId,
    string Name,
    string EffectiveStatus,
    DateOnly PaidThrough,
    int? DaysUntilDisabled,
    int? DaysUntilExpiry);

/// <summary>"Arranque": gyms con Owners sin activar o con el onboarding sin terminar.</summary>
public sealed record AdminStartupBlock(int Total, IReadOnlyList<StartupGymRow> Items);

/// <param name="PendingOwnerUserId">El Owner sin activar al que apunta "Reenviar acceso"; nulo si todos activaron.</param>
public sealed record StartupGymRow(
    Guid GymId,
    string Name,
    int DaysSinceCreated,
    bool OwnerNotActivated,
    bool OnboardingPending,
    Guid? PendingOwnerUserId,
    string? PendingOwnerEmail);

/// <summary>"Límites": gyms al 90 % o más de su límite de miembros.</summary>
public sealed record AdminLimitsBlock(int Total, IReadOnlyList<LimitGymRow> Items);

public sealed record LimitGymRow(Guid GymId, string Name, int CountedMembers, int MemberLimit, int UsagePercent);

public sealed record PortfolioSummary(
    IReadOnlyDictionary<string, int> CountByStatus,
    int ExpiringWithin15Days);

/// <param name="Mrr">Ingreso recurrente mensual: suma de los precios de las suscripciones al día o en mora (igual que la foto mensual).</param>
/// <param name="Pending">Cartera pendiente: lo contratado menos lo cobrado este mes, nunca negativo.</param>
public sealed record PlatformRevenue(
    decimal Contracted,
    decimal CollectedThisMonth,
    decimal CollectedLastMonth,
    decimal Mrr,
    decimal Pending);

public sealed record GymUsageRow(
    Guid GymId,
    string Name,
    string EffectiveStatus,
    int ActiveMembers,
    int CheckInsThisMonth,
    decimal MemberRevenueThisMonth,
    DateOnly? LastCheckIn,
    int? CountedMembers,
    int? MemberLimit,
    bool IsInactive,
    int? DaysSinceLastCheckIn);

public sealed record PlatformTotals(
    int GymCount,
    int ActiveMembers,
    int CheckInsThisMonth);

// --- Impersonación ------------------------------------------------------------------------

/// <param name="Reason">Obligatorio.</param>
/// <param name="Mode"><c>read</c> (por defecto) o <c>write</c>.</param>
public sealed record OpenImpersonationRequest(string Reason, string Mode = "read");

public sealed record ImpersonationTokenResponse(
    string Token,
    DateTimeOffset ExpiresAtUtc,
    Guid SessionId,
    Guid GymId,
    string Mode,
    string TokenType = "Bearer");

public sealed record ImpersonationItem(
    Guid Id,
    Guid SuperAdminUserId,
    Guid GymId,
    string GymName,
    string Reason,
    string Mode,
    int ActionCount,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt);

/// <summary>Una foto mensual. Month es el primer día del mes; Churn es null sin gyms activos al inicio.</summary>
public sealed record MonthlyMetricItem(
    DateOnly Month,
    decimal Mrr,
    decimal Collected,
    int NewGyms,
    int TrialGymsAtClose,
    int ConvertedGyms,
    int LostGyms,
    int TrialsNotConverted,
    int ActiveGymsAtStart,
    int ActiveGymsAtClose,
    decimal? Churn,
    int ActiveMembers,
    int CheckIns);

public sealed record MonthlyMetricsResponse(IReadOnlyList<MonthlyMetricItem> Months);

/// <summary>Una petición hecha durante la sesión (consulta o escritura).</summary>
public sealed record ImpersonationActionItem(
    Guid Id,
    string Method,
    string Endpoint,
    string Summary,
    int StatusCode,
    DateTimeOffset OccurredAt);


/// <summary>
/// Lo que el Owner ve de la suscripción de su gym (solo lectura): plan, precio, estado efectivo de hoy, hasta
/// cuándo está cubierto, uso del límite de miembros y los últimos pagos. No expone las notas internas.
/// </summary>
/// <param name="Plan">Nombre del plan del SaaS ("Sin plan" si no tiene uno).</param>
/// <param name="EffectiveStatus">Nombre de <c>EffectiveSubscriptionStatus</c>: Trial, TrialEnded, Active, Overdue, Disabled, Suspended.</param>
/// <param name="SuspensionReason">Motivo de la suspensión; solo viene si el estado es Suspended.</param>
/// <param name="TrialEndsOn">Fin de la prueba; nulo si el gym nació sin prueba.</param>
/// <param name="MemberLimit">Límite efectivo de miembros. Nulo = sin límite.</param>
public sealed record OwnSubscriptionResponse(
    string Plan,
    decimal MonthlyPrice,
    string EffectiveStatus,
    string? SuspensionReason,
    DateOnly? TrialEndsOn,
    DateOnly PaidThrough,
    int? DaysUntilDisabled,
    int? MemberLimit,
    int CountedMembers,
    IReadOnlyList<OwnSubscriptionPayment> RecentPayments);

public sealed record OwnSubscriptionPayment(
    Guid Id,
    decimal Amount,
    string Method,
    DateOnly CoversFrom,
    DateOnly CoversThrough,
    DateTimeOffset PaidAt);
