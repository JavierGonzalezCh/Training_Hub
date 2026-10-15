namespace GSM.Application.Memberships.Contracts;

/// <param name="DurationValue">Cuántos días o meses dura el plan.</param>
/// <param name="DurationUnit"><c>days</c> (por defecto si falta) o <c>months</c> (meses del calendario).</param>
public sealed record CreatePlanRequest(
    string Name, string? Description, decimal Price, int DurationValue, string? DurationUnit = null);

/// <inheritdoc cref="CreatePlanRequest"/>
public sealed record UpdatePlanRequest(
    string Name, string? Description, decimal Price, int DurationValue, string? DurationUnit = null);

/// <param name="DurationUnit"><c>days</c> o <c>months</c>.</param>
public sealed record PlanResponse(
    Guid Id,
    string Name,
    string? Description,
    decimal Price,
    string DurationUnit,
    int DurationValue,
    bool IsActive,
    DateTimeOffset CreatedAt);

/// <summary>
/// <c>StartDate</c> es opcional: el sistema calcula el inicio según la situación del miembro (§4.1).
/// Si el staff lo cambia, <c>StartDateReason</c> es obligatorio (salvo en la primera membresía).
/// </summary>
/// <param name="AgreedPrice">Precio acordado; nulo = el del plan. No puede superarlo; si es menor, <c>DiscountReason</c> es obligatorio.</param>
/// <param name="WaiveEnrollment">Exonera la matrícula (solo tiene efecto si aplica: primera membresía del miembro). Exige <c>EnrollmentWaivedReason</c>.</param>
public sealed record AssignMembershipRequest(
    Guid PlanId,
    DateOnly? StartDate,
    string? StartDateReason = null,
    decimal? AgreedPrice = null,
    string? DiscountReason = null,
    bool WaiveEnrollment = false,
    string? EnrollmentWaivedReason = null);

/// <summary>Vista previa del inicio y el fin de una membresía antes de confirmarla.</summary>
/// <param name="Kind"><c>new</c>, <c>early</c>, <c>in_grace</c> o <c>after_grace</c>.</param>
/// <param name="SystemStartDate">Inicio que calcula el sistema.</param>
/// <param name="StartDate">Inicio que se usaría (el del staff si lo cambió).</param>
/// <param name="PreviousEndDate">Fin de la membresía anterior, si la hay.</param>
/// <param name="EnrollmentFee">Matrícula que se sumaría (la del gym si el miembro nunca tuvo membresía; 0 si no aplica).</param>
/// <param name="PlanPrice">Precio de lista del plan.</param>
public sealed record RenewalPreviewResponse(
    string Kind,
    DateOnly SystemStartDate,
    DateOnly StartDate,
    DateOnly EndDate,
    DateOnly? PreviousEndDate,
    bool StartDateChanged,
    decimal EnrollmentFee,
    decimal PlanPrice);

public sealed record MembershipResponse(
    Guid Id,
    Guid MemberId,
    Guid PlanId,
    string PlanName,
    string Status,
    DateOnly StartDate,
    DateOnly EndDate,
    DateTimeOffset CreatedAt,
    DateOnly? PausedOn,
    string? CancellationReason,
    string RenewalKind,
    string? StartOverrideReason,
    decimal AgreedPrice,
    string? DiscountReason,
    decimal EnrollmentFee,
    string? EnrollmentWaivedReason,
    decimal TotalToCharge,
    int? CycleDay);

public sealed record MembershipSettingsResponse(int GraceDays, int ExpiringDays, decimal EnrollmentFee);

public sealed record UpdateMembershipSettingsRequest(int GraceDays, int ExpiringDays, decimal EnrollmentFee);

/// <summary>Estado comercial y veredicto de acceso de un miembro hoy (recepción, ficha).</summary>
/// <param name="Commercial"><c>no_plan</c>, <c>current</c>, <c>expiring</c>, <c>grace</c>, <c>expired</c>, <c>frozen</c> o <c>scheduled</c>.</param>
/// <param name="Kind"><c>covered</c>, <c>expiring</c>, <c>grace</c> o <c>blocked</c>.</param>
/// <param name="BlockReason"><c>no_membership</c>, <c>expired</c>, <c>frozen</c> o <c>scheduled</c> (su membresía empieza el <c>StartsOn</c>). Solo si está bloqueado.</param>
public sealed record AccessStatusResponse(
    string Commercial,
    string Kind,
    string? BlockReason,
    bool CanEnter,
    DateOnly? EndsOn,
    int? DaysLeft,
    DateOnly? GraceEndsOn,
    int? GraceDaysLeft,
    string? Notice,
    string? BlockMessage,
    DateOnly? StartsOn);

public sealed record CancelMembershipRequest(string Reason);

public sealed record ExpiringMembershipItem(
    Guid MembershipId,
    Guid MemberId,
    string MemberName,
    string PlanName,
    DateOnly EndDate,
    int DaysLeft);
