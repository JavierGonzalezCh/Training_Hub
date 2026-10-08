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
    int? DaysUntilDisabled);

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

public sealed record SubscriptionDetail(
    string Plan,
    decimal MonthlyPrice,
    DateOnly StartedOn,
    DateOnly? TrialEndsOn,
    DateOnly PaidThrough,
    string EffectiveStatus,
    string ManualStatus,
    int? DaysUntilDisabled,
    string? Notes);

public sealed record UpdateSubscriptionRequest(GymPlan Plan, decimal MonthlyPrice);

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

public sealed record OpenImpersonationRequest(string Reason);

public sealed record ImpersonationTokenResponse(
    string Token,
    DateTimeOffset ExpiresAtUtc,
    Guid SessionId,
    Guid GymId,
    string TokenType = "Bearer");

public sealed record ImpersonationItem(
    Guid Id,
    Guid SuperAdminUserId,
    Guid GymId,
    string GymName,
    string Reason,
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
    int ActiveGymsAtStart,
    int ActiveGymsAtClose,
    decimal? Churn,
    int ActiveMembers,
    int CheckIns);

public sealed record MonthlyMetricsResponse(IReadOnlyList<MonthlyMetricItem> Months);
