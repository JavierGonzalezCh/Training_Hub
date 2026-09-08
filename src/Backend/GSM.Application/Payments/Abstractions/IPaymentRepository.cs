using GSM.Domain.Payments;

namespace GSM.Application.Payments.Abstractions;

public sealed record PaymentPageQuery(
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    PaymentMethod? Method,
    Guid? MemberId,
    int Page,
    int PageSize);

/// <summary>Acceso de escritura/consulta del módulo Payments. Sin <c>SaveChanges</c>. Los pagos son inmutables.</summary>
public interface IPaymentRepository
{
    void Add(Payment payment);

    Task<Payment?> GetByIdAsync(Guid paymentId, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Payment> Items, int Total)> GetPageAsync(
        PaymentPageQuery query, CancellationToken cancellationToken = default);

    /// <summary>Todos los pagos del gym cuyo <c>paid_at</c> cae en <c>[fromUtc, toUtc)</c> (cierre de caja).</summary>
    Task<IReadOnlyList<Payment>> GetInRangeAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);
}
