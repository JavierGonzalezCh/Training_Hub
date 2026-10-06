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
}

public sealed record VoidPaymentRequest(string Reason);

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
