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

    Task<bool> IsReversedAsync(Guid paymentId, CancellationToken cancellationToken = default);

    /// <summary>De <paramref name="paymentIds"/>, los que ya tienen una anulación.</summary>
    Task<IReadOnlySet<Guid>> GetReversedIdsAsync(
        IReadOnlyCollection<Guid> paymentIds, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Payment> Items, int Total)> GetPageAsync(
        PaymentPageQuery query, CancellationToken cancellationToken = default);

    /// <summary>Todos los asientos ligados a un turno (cualquier método), del más antiguo al más reciente.</summary>
    Task<IReadOnlyList<Payment>> GetByShiftAsync(Guid shiftId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Efectivo del turno que mueve el cajón: <c>CashIn</c> suma los asientos en efectivo positivos y
    /// <c>CashOut</c> la magnitud de los negativos (devoluciones y anulaciones). Solo los que
    /// <see cref="Payment.AffectsCashDrawer"/>.
    /// </summary>
    Task<(decimal CashIn, decimal CashOut)> GetCashDrawerTotalsAsync(
        Guid shiftId, CancellationToken cancellationToken = default);

    /// <summary>Todos los pagos del gym cuyo <c>paid_at</c> cae en <c>[fromUtc, toUtc)</c> (cierre de caja).</summary>
    Task<IReadOnlyList<Payment>> GetInRangeAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);
}
