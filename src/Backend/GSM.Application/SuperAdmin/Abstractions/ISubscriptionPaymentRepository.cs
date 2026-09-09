using GSM.Domain.Payments;
using GSM.Domain.SuperAdmin;

namespace GSM.Application.SuperAdmin.Abstractions;

/// <summary>
/// Acceso de escritura/consulta a los pagos del SaaS. Sin <c>SaveChanges</c>. Los pagos son
/// inmutables: solo <c>Add</c> y lecturas.
/// </summary>
public interface ISubscriptionPaymentRepository
{
    void Add(SubscriptionPayment payment);

    /// <summary>Histórico de un gym, del más reciente al más antiguo.</summary>
    Task<IReadOnlyList<SubscriptionPaymentRow>> GetByGymAsync(
        Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>Todos los cobros cuyo <c>paid_at</c> cae en <c>[fromUtc, toUtc)</c>, con el nombre del gym.</summary>
    Task<IReadOnlyList<PlatformPaymentRow>> GetInRangeAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);

    /// <summary>Suma de montos de todos los gyms cuyo <c>paid_at</c> cae en <c>[fromUtc, toUtc)</c> (panel).</summary>
    Task<decimal> SumInRangeAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);
}

public sealed record SubscriptionPaymentRow(
    Guid Id,
    Guid GymId,
    decimal Amount,
    PaymentMethod Method,
    DateOnly CoversFrom,
    DateOnly CoversThrough,
    DateTimeOffset PaidAt,
    Guid RegisteredBy,
    string? Reference,
    string? Notes);

public sealed record PlatformPaymentRow(
    Guid Id,
    Guid GymId,
    string GymName,
    decimal Amount,
    PaymentMethod Method,
    DateOnly CoversFrom,
    DateOnly CoversThrough,
    DateTimeOffset PaidAt,
    Guid RegisteredBy);
