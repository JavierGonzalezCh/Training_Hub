using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Common.Events;

/// <inheritdoc cref="IDomainEventPublisher" />
public sealed class DomainEventPublisher(
    IServiceProvider serviceProvider,
    ILogger<DomainEventPublisher> logger) : IDomainEventPublisher
{
    private static readonly ConcurrentDictionary<Type, (Type HandlerType, MethodInfo Handle)> Cache = new();

    public async Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        var (handlerType, handle) = Cache.GetOrAdd(domainEvent.GetType(), static eventType =>
        {
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
            var handle = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!;
            return (handlerType, handle);
        });

        foreach (var handler in serviceProvider.GetServices(handlerType))
        {
            if (handler is null)
            {
                continue;
            }

            try
            {
                await (Task)handle.Invoke(handler, [domainEvent, cancellationToken])!;
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "El handler {Handler} falló procesando el evento {Event}; la operación que lo publicó ya está confirmada.",
                    handler.GetType().Name, domainEvent.GetType().Name);
            }
        }
    }

    public async Task PublishAsync(
        IEnumerable<IDomainEvent> domainEvents,
        CancellationToken cancellationToken = default)
    {
        foreach (var domainEvent in domainEvents)
        {
            await PublishAsync(domainEvent, cancellationToken);
        }
    }
}
