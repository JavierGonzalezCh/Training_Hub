using GSM.Domain.Payments;

namespace GSM.Application.Payments.Abstractions;

/// <summary>Acceso a las solicitudes de anulación (<c>payments.void_requests</c>). Sin <c>SaveChanges</c>.</summary>
public interface IVoidRequestRepository
{
    void Add(VoidRequest request);

    Task<VoidRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>La solicitud pendiente de ese pago, si la hay (con seguimiento, para resolverla).</summary>
    Task<VoidRequest?> GetPendingForPaymentAsync(Guid paymentId, CancellationToken cancellationToken = default);

    /// <summary>De <paramref name="paymentIds"/>, los que tienen una solicitud pendiente.</summary>
    Task<IReadOnlySet<Guid>> GetPendingPaymentIdsAsync(
        IReadOnlyCollection<Guid> paymentIds, CancellationToken cancellationToken = default);

    /// <summary>Pendientes primero, luego las resueltas; dentro de cada grupo, las más recientes arriba.</summary>
    Task<(IReadOnlyList<VoidRequest> Items, int Total)> GetPageAsync(
        VoidRequestStatus? status, int page, int pageSize, CancellationToken cancellationToken = default);
}
