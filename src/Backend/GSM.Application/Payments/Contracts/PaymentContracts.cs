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
}

public sealed record VoidPaymentRequest(string Reason);

/// <summary>Pide anular un pago. El motivo es obligatorio (lo valida el dominio: 422).</summary>
public sealed record RequestVoidRequest(string Reason);

/// <summary>
/// Solicitud de anulación con un resumen del pago para decidir sin abrirlo. <c>Status</c> es
/// <c>pending</c>, <c>approved</c> o <c>rejected</c> (texto, no enum: NSwag infiere mal los enums de cuerpos).
/// </summary>
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
    DateTimeOffset PaymentPaidAt);

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
    IReadOnlyList<PaymentResponse> Payments);
