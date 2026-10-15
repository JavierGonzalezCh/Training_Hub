using GSM.Domain.Gyms;
using GSM.Domain.Payments;

namespace GSM.Application.SuperAdmin.Contracts;

/// <summary>Filtro opcional de la cartera por estado efectivo (nombre del enum, case-insensitive).</summary>
public sealed record PortfolioQuery
{
    public string? Status { get; init; }
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
    int CountedMembers);

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
    SubscriptionDetail Subscription);

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

/// <param name="Plan">Enum heredado (<c>basic</c>/<c>pro</c>/<c>enterprise</c>). Si viene <paramref name="SaasPlanId"/> manda este.</param>
/// <param name="SaasPlanId">Plan del SaaS (S2). Si se omite, se resuelve por el nombre de <paramref name="Plan"/>.</param>
/// <param name="MemberLimitOverride">Límite propio del cliente; nulo = el del plan (el PUT reemplaza el valor anterior).</param>
/// <param name="TrialGraceDays">Días de <c>trial_ended</c> tras la prueba (0 a 60). Nulo = no cambia el valor actual.</param>
public sealed record UpdateSubscriptionRequest(
    GymPlan Plan,
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
    PlatformTotals Platform);

public sealed record PortfolioSummary(
    IReadOnlyDictionary<string, int> CountByStatus,
    int ExpiringWithin15Days);

public sealed record PlatformRevenue(
    decimal Contracted,
    decimal CollectedThisMonth,
    decimal CollectedLastMonth);

public sealed record GymUsageRow(
    Guid GymId,
    string Name,
    string EffectiveStatus,
    int ActiveMembers,
    int CheckInsThisMonth,
    decimal MemberRevenueThisMonth,
    DateOnly? LastCheckIn);

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
