using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using GSM.Application.Common.Logs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Common.Events;

/// <inheritdoc cref="IDomainEventPublisher" />
public sealed class DomainEventPublisher(
    IServiceProvider serviceProvider,
    ILogStore logStore,
    ILogger<DomainEventPublisher> logger) : IDomainEventPublisher
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

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

                RecordFailure(domainEvent, handler, ex);
            }
        }
    }

    /// <summary>Guarda el fallo con el evento serializado para poder reintentarlo; nunca lanza.</summary>
    private void RecordFailure(IDomainEvent domainEvent, object handler, Exception exception)
    {
        try
        {
            logStore.RecordEventHandlerFailure(new EventHandlerFailureLogEntry(
                domainEvent.GetType().FullName ?? domainEvent.GetType().Name,
                handler.GetType().FullName ?? handler.GetType().Name,
                JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), PayloadOptions),
                exception));
        }
        catch (Exception logEx)
        {
            logger.LogError(logEx, "No se pudo registrar el fallo del handler {Handler}", handler.GetType().Name);
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