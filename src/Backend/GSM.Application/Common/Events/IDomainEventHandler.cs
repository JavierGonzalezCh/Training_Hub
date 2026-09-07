namespace GSM.Application.Common.Events;

/// <summary>
/// Reacción in-process a un evento de otro módulo. Un evento puede tener 0..N handlers.
/// Se registran por assembly scanning en <c>AddApplication()</c>.
/// </summary>
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
}
