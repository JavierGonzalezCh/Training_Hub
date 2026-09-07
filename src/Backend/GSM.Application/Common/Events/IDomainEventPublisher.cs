namespace GSM.Application.Common.Events;

/// <summary>
/// Despacha eventos a sus handlers. Se invoca **después** de que el caso de uso commiteó
/// su trabajo, nunca dentro de la misma transacción. Un handler que falla se loguea y no
/// propaga: la operación que publicó el evento ya está confirmada.
/// </summary>
public interface IDomainEventPublisher
{
    Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);

    Task PublishAsync(
        IEnumerable<IDomainEvent> domainEvents,
        CancellationToken cancellationToken = default);
}
