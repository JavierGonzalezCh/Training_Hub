using GSM.Domain.Payments;

namespace GSM.Application.Payments.Contracts;

public sealed record RecordPaymentRequest(
    decimal Amount,
    PaymentMethod Method,
    string Concept,
    string? Notes,
    Guid? MemberId,
    Guid? MembershipId);

public sealed record PaymentResponse(
    Guid Id,
    Guid? MemberId,
    Guid? MembershipId,
    decimal Amount,
    string Method,
    string Concept,
    string? Notes,
    Guid RegisteredBy,
    DateTimeOffset PaidAt,
    Guid? ReversesPaymentId)
{
    /// <summary>Ya existe una anulación de este pago. No viene de la entidad: lo resuelve el caso de uso.</summary>
    public bool IsReversed { get; init; }

    /// <summary>Hay una solicitud de anulación pendiente ("anulación solicitada"). La resuelve el caso de uso.</summary>
    public bool HasPendingVoidRequest { get; init; }

    /// <summary>Turno de caja en que se registró el asiento (<c>null</c> si no había caja abierta).</summary>
    public Guid? ShiftId { get; init; }

    /// <summary>¿Mueve el efectivo del cajón del turno? Una anulación "sin devolución" queda en <c>false</c>.</summary>
    public bool AffectsCashDrawer { get; init; }

    /// <summary>Nombre de quien cobró. Solo lo llenan las pantallas que lo muestran (cierre diario, reporte de turno).</summary>
    public string? RegisteredByName { get; init; }

    /// <summary>
    /// Nombre del miembro que pagó. Solo lo llena la lista de pagos del gym: sin él, cinco "Plan Mensual" iguales
    /// no se distinguen y se puede anular el equivocado. Nulo en ventas sin miembro o si el miembro no se encontró.
    /// </summary>
    public string? MemberName { get; init; }
}

/// <summary>
/// <c>RefundNow</c> solo cuenta al anular un pago en efectivo de un turno ya cerrado: <c>true</c> = el dinero se
/// devuelve ahora (sale de la caja abierta); <c>false</c> = el dinero nunca estuvo (error de registro, no toca la
/// caja actual). Sin valor, ese caso es 422.
/// </summary>
public sealed record VoidPaymentRequest(string Reason, bool? RefundNow = null);

/// <summary>Cuerpo opcional de la aprobación: la misma decisión de <see cref="VoidPaymentRequest.RefundNow"/>.</summary>
public sealed record ApproveVoidRequestRequest(bool? RefundNow);

/// <summary>Pide anular un pago. El motivo es obligatorio (lo valida el dominio: 422).</summary>
public sealed record RequestVoidRequest(string Reason);

/// <summary>
/// Solicitud de anulación con un resumen del pago para decidir sin abrirlo. <c>Status</c> es
/// <c>pending</c>, <c>approved</c> o <c>rejected</c> (texto, no enum: NSwag infiere mal los enums de cuerpos).
/// </summary>
/// <summary>Cuerpo opcional del rechazo: una nota para quien pidió la anulación.</summary>
public sealed record RejectVoidRequestRequest(string? Note);

public sealed record VoidRequestResponse(
    Guid Id,
    Guid PaymentId,
    string Reason,
    Guid RequestedBy,
    string? RequestedByName,
    DateTimeOffset RequestedAt,
    string Status,
    Guid? ResolvedBy,
    string? ResolvedByName,
    DateTimeOffset? ResolvedAt,
    string PaymentConcept,
    decimal PaymentAmount,
    string PaymentMethod,
    Guid? PaymentMemberId,
    DateTimeOffset PaymentPaidAt,
    string? RejectionNote = null)
{
    /// <summary>
    /// Aprobarla exige decidir si el dinero se devuelve ahora (pago en efectivo de un turno ya cerrado).
    /// Lo resuelve el caso de uso de lectura.
    /// </summary>
    public bool RequiresRefundDecision { get; init; }
}

public sealed record ListVoidRequestsQuery
{
    /// <summary><c>pending</c>, <c>approved</c> o <c>rejected</c>; sin valor, todas.</summary>
    public string? Status { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed record ListPaymentsQuery
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public PaymentMethod? Method { get; init; }
    public Guid? MemberId { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed record DailyCashRegisterResponse(
    DateOnly Date,
    decimal Total,
    int TransactionCount,
    IReadOnlyDictionary<string, decimal> TotalsByMethod,
    IReadOnlyList<PaymentResponse> Payments)
{
    /// <summary>Totales por método con la cantidad de pagos, para conciliar contra los extractos (§5).</summary>
    public IReadOnlyList<CashMethodSummary> Methods { get; init; } = [];

    /// <summary>Turnos abiertos ese día, con sus diferencias (§8).</summary>
    public IReadOnlyList<CashShiftResponse> Shifts { get; init; } = [];

    /// <summary>Retiros e ingresos de efectivo de esos turnos.</summary>
    public IReadOnlyList<CashMovementResponse> Movements { get; init; } = [];

    /// <summary>Asientos de anulación del día, con su motivo en <c>Notes</c>.</summary>
    public IReadOnlyList<PaymentResponse> Reversals { get; init; } = [];
}
