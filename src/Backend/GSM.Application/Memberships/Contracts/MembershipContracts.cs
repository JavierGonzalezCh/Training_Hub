namespace GSM.Application.Memberships.Contracts;

/// <param name="DurationValue">Cuántos días o meses dura el plan. En una tiquetera, ignorado: manda <c>ValidityDays</c>.</param>
/// <param name="DurationUnit"><c>days</c> (por defecto si falta) o <c>months</c> (meses del calendario).</param>
/// <param name="MaxFreezeDays">Total de días que se puede congelar una membresía del plan; 0 o nulo = no se congela (en una edición, nulo = no cambia).</param>
/// <param name="MinFreezeDays">Mínimo de días por congelamiento (1 por defecto).</param>
/// <param name="Kind"><c>time</c> (por defecto si falta) o <c>pass</c> (tiquetera). En una edición no puede cambiar.</param>
/// <param name="Entries">Entradas que trae. Obligatorio en una tiquetera; en un plan por tiempo no se manda.</param>
/// <param name="ValidityDays">Días en que se deben usar las entradas. Obligatorio en una tiquetera.</param>
/// <param name="Seats">Cupos (MS7): 1 = individual (por defecto); más de 1 = plan de grupo, solo por tiempo. En una edición, nulo = no cambia.</param>
public sealed record CreatePlanRequest(
    string Name, string? Description, decimal Price, int DurationValue, string? DurationUnit = null,
    int? MaxFreezeDays = null, int? MinFreezeDays = null, string? Kind = null, int? Entries = null,
    int? ValidityDays = null, int? Seats = null);

/// <inheritdoc cref="CreatePlanRequest"/>
public sealed record UpdatePlanRequest(
    string Name, string? Description, decimal Price, int DurationValue, string? DurationUnit = null,
    int? MaxFreezeDays = null, int? MinFreezeDays = null, string? Kind = null, int? Entries = null,
    int? ValidityDays = null, int? Seats = null);

/// <param name="DurationUnit"><c>days</c> o <c>months</c>.</param>
/// <param name="Kind"><c>time</c> o <c>pass</c> (tiquetera).</param>
/// <param name="Entries">Entradas que trae, solo tiqueteras.</param>
/// <param name="ValidityDays">Días de vigencia de las entradas, solo tiqueteras (igual a <c>DurationValue</c>).</param>
public sealed record PlanResponse(
    Guid Id,
    string Name,
    string? Description,
    decimal Price,
    string DurationUnit,
    int DurationValue,
    bool IsActive,
    DateTimeOffset CreatedAt,
    int MaxFreezeDays,
    int MinFreezeDays,
    string Kind,
    int? Entries,
    int? ValidityDays,
    int Seats);

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
    string? CancellationReason,
    string RenewalKind,
    string? StartOverrideReason,
    decimal AgreedPrice,
    string? DiscountReason,
    decimal EnrollmentFee,
    string? EnrollmentWaivedReason,
    decimal TotalToCharge,
    int? CycleDay,
    MembershipFreezeResponse? ActiveFreeze,
    MembershipFreezeResponse? NextFreeze,
    int? EntriesTotal,
    int? EntriesUsed,
    int? EntriesLeft,
    decimal CreditApplied,
    string? CreditAdjustmentReason,
    Guid? ReplacesMembershipId,
    Guid? GroupId,
    bool IsGroupHolder,
    /// <summary>
    /// MS2-3: lo que falta por cobrar (total a cobrar − lo pagado), solo si es mayor que 0 y la membresía no está
    /// cancelada; nulo si está paga. Solo lo trae la lista de la ficha.
    /// </summary>
    decimal? AmountDue = null);

/// <summary>
/// Congela una membresía (§5.2). Sin fechas = abierto desde hoy (termina al reanudar). Con
/// <c>EndsOn</c> es programado (<c>StartsOn</c> por defecto hoy). <c>OverrideLimit</c> solo lo permite el Owner.
/// </summary>
public sealed record CreateFreezeRequest(
    DateOnly? StartsOn, DateOnly? EndsOn, string Reason, bool OverrideLimit = false);

/// <param name="State"><c>scheduled</c>, <c>active</c> o <c>ended</c>.</param>
/// <param name="EndsOn">Nulo = abierto (termina al reanudar).</param>
public sealed record MembershipFreezeResponse(
    Guid Id,
    Guid MembershipId,
    DateOnly StartsOn,
    DateOnly? EndsOn,
    string Reason,
    bool OverrideLimit,
    int DaysCredited,
    string State,
    DateTimeOffset CreatedAt,
    Guid? CreatedBy);

/// <summary>Historial de congelamientos de una membresía y lo que le queda según su plan.</summary>
public sealed record MembershipFreezesResponse(
    int MaxFreezeDays,
    int MinFreezeDays,
    int DaysUsed,
    int DaysRemaining,
    IReadOnlyList<MembershipFreezeResponse> Items);

public sealed record MembershipSettingsResponse(int GraceDays, int ExpiringDays, decimal EnrollmentFee);

public sealed record UpdateMembershipSettingsRequest(int GraceDays, int ExpiringDays, decimal EnrollmentFee);

/// <summary>Estado comercial y veredicto de acceso de un miembro hoy (recepción, ficha).</summary>
/// <param name="Commercial"><c>no_plan</c>, <c>current</c>, <c>expiring</c>, <c>grace</c>, <c>expired</c>, <c>frozen</c> o <c>scheduled</c>.</param>
/// <param name="Kind"><c>covered</c>, <c>expiring</c>, <c>grace</c> o <c>blocked</c>.</param>
/// <param name="BlockReason"><c>no_membership</c>, <c>expired</c>, <c>frozen</c>, <c>scheduled</c> (su membresía empieza el <c>StartsOn</c>) o <c>pass_exhausted</c> (tiquetera sin entradas). Solo si está bloqueado.</param>
/// <param name="EntriesLeft">Entradas que le quedan, si lo cubre una tiquetera (MS5).</param>
/// <param name="EntriesTotal">Entradas con que empezó esa tiquetera.</param>
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
    DateOnly? StartsOn,
    int? EntriesLeft,
    int? EntriesTotal);

public sealed record CancelMembershipRequest(string Reason);

/// <summary>
/// Cambio de plan de la membresía vigente (MS6, §8). <c>AgreedPrice</c>/<c>DiscountReason</c> como al asignar.
/// <c>CreditOverride</c> ajusta el crédito que calcula el sistema (entre 0 y lo pagado por el plan) y exige
/// <c>CreditAdjustmentReason</c>.
/// </summary>
public sealed record ChangePlanRequest(
    Guid PlanId,
    decimal? AgreedPrice = null,
    string? DiscountReason = null,
    decimal? CreditOverride = null,
    string? CreditAdjustmentReason = null);

/// <summary>
/// Vista previa de un cambio de plan, sin crear nada: crédito de lo no usado, la membresía nueva y qué se cobra o se
/// devuelve. Con tiquetera, <c>DaysLeft</c>/<c>DaysTotal</c> son nulos y manda <c>EntriesLeft</c>/<c>EntriesTotal</c>.
/// </summary>
/// <param name="AmountPaid">Pagos netos ligados a la membresía vigente (las anulaciones ya restan).</param>
/// <param name="CreditBase">Parte del plan que se considera pagada (sin matrícula).</param>
/// <param name="CalculatedCredit">Crédito que calcula el sistema.</param>
/// <param name="Credit">Crédito que se usaría (el ajustado por el staff, si lo hay).</param>
/// <param name="CreditApplied">Parte del crédito que descuenta de la membresía nueva.</param>
/// <param name="TotalToCharge">Lo que se cobra: precio acordado − crédito aplicado (nunca negativo).</param>
/// <param name="RefundAmount">Lo que se devuelve al miembro cuando el crédito supera el precio del plan nuevo.</param>
public sealed record PlanChangePreviewResponse(
    Guid CurrentMembershipId,
    string CurrentPlanName,
    DateOnly CurrentEndDate,
    decimal AmountPaid,
    decimal CreditBase,
    decimal CalculatedCredit,
    decimal Credit,
    decimal CreditApplied,
    int? DaysLeft,
    int? DaysTotal,
    int? EntriesLeft,
    int? EntriesTotal,
    Guid NewPlanId,
    string NewPlanName,
    decimal PlanPrice,
    decimal AgreedPrice,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalToCharge,
    decimal RefundAmount);

/// <summary>
/// Resultado del cambio: la anterior (cancelada) y la nueva. La UI cobra <c>TotalToCharge</c> o registra la
/// devolución de <c>RefundAmount</c> en Payments (dos llamadas, no una escritura cruzada). La devolución se liga a
/// la membresía anterior.
/// </summary>
public sealed record ChangePlanResponse(
    MembershipResponse Previous,
    MembershipResponse Current,
    decimal CreditApplied,
    decimal TotalToCharge,
    decimal RefundAmount);

/// <summary>
/// Asigna un plan de grupo (MS7, §9): el titular paga y los beneficiarios (hasta <c>Seats - 1</c>, pueden ser menos y
/// se completan después) reciben su membresía con las mismas fechas. Los demás campos son los de
/// <see cref="AssignMembershipRequest"/> y valen para el grupo, con el titular como el miembro cuyo historial manda.
/// </summary>
/// <param name="HolderMemberId">El que paga: lleva el precio acordado y la matrícula, si aplica.</param>
/// <param name="BeneficiaryMemberIds">Los demás miembros del grupo. Sin repetidos ni el titular.</param>
public sealed record AssignGroupMembershipRequest(
    Guid HolderMemberId,
    Guid PlanId,
    IReadOnlyList<Guid> BeneficiaryMemberIds,
    DateOnly? StartDate,
    string? StartDateReason = null,
    decimal? AgreedPrice = null,
    string? DiscountReason = null,
    bool WaiveEnrollment = false,
    string? EnrollmentWaivedReason = null);

/// <summary>
/// Renueva un grupo (MS7): crea otro grupo que sigue al anterior con el mismo titular. Los beneficiarios por defecto
/// son los que siguen en el grupo; si <c>BeneficiaryMemberIds</c> viene, esa es la lista nueva.
/// </summary>
/// <param name="PlanId">Plan de grupo de la renovación; nulo = el mismo del grupo.</param>
/// <param name="BeneficiaryMemberIds">Lista nueva de beneficiarios; nulo = los actuales.</param>
public sealed record RenewGroupMembershipRequest(
    Guid? PlanId,
    IReadOnlyList<Guid>? BeneficiaryMemberIds,
    DateOnly? StartDate,
    string? StartDateReason = null,
    decimal? AgreedPrice = null,
    string? DiscountReason = null);

/// <summary>Mete a otro miembro al grupo mientras haya cupo: hereda las fechas actuales del grupo.</summary>
public sealed record AddGroupBeneficiaryRequest(Guid MemberId);

/// <param name="Membership">Su membresía dentro del grupo (la del titular lleva el precio; las de los beneficiarios, 0).</param>
public sealed record MembershipGroupMemberResponse(
    Guid MemberId,
    string MemberName,
    bool IsHolder,
    MembershipResponse Membership);

/// <summary>
/// Un grupo (MS7). <c>SeatsUsed</c> cuenta las membresías no canceladas; los cupos libres se llenan con
/// <c>POST /api/membership-groups/{id}/beneficiaries</c>. <c>StartDate</c>/<c>EndDate</c>/<c>TotalToCharge</c> son los de la
/// membresía del titular: lo que la UI cobra al asignar o renovar.
/// </summary>
public sealed record MembershipGroupResponse(
    Guid Id,
    Guid PlanId,
    string PlanName,
    int Seats,
    int SeatsUsed,
    int SeatsFree,
    Guid HolderMemberId,
    Guid HolderMembershipId,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalToCharge,
    IReadOnlyList<MembershipGroupMemberResponse> Members);

public sealed record ExpiringMembershipItem(
    Guid MembershipId,
    Guid MemberId,
    string MemberName,
    string PlanName,
    DateOnly EndDate,
    int DaysLeft);
