using System.Text.Json;
using System.Text.Json.Serialization;
using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Common.Logs;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Logs;

public sealed record ReplayResult(string Outcome, string? Message);

/// <summary>
/// Vuelve a ejecutar <b>un manejador</b> con su evento (docs/GSM-logs.md §4.3). El evento se reconstruye del payload
/// guardado, pero lo que el log limpia o puede estar viejo se vuelve a pedir al módulo dueño con un
/// <see cref="IEventRetryRefresher{TEvent}"/>; sin refrescador, un evento con campos limpiados no se reintenta.
/// El manejador se ejecuta directo (no por el publicador), así que si falla otra vez no genera otra fila.
/// </summary>
public sealed class EventFailureReplayer(IServiceProvider services, ILogger<EventFailureReplayer> logger)
{
    // Las mismas opciones con que DomainEventPublisher serializa el evento.
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<ReplayResult> ReplayAsync(EventFailureRecord failure, CancellationToken cancellationToken)
    {
        var eventType = ResolveEventType(failure.EventType);
        if (eventType is null)
        {
            return NotRetryable("El tipo del evento ya no existe en el sistema.");
        }

        if (failure.Payload.Contains("\"_truncated\"", StringComparison.Ordinal))
        {
            return NotRetryable("El evento era demasiado grande y no se guardó completo.");
        }

        IDomainEvent? domainEvent;
        try
        {
            domainEvent = JsonSerializer.Deserialize(failure.Payload, eventType, PayloadOptions) as IDomainEvent;
        }
        catch (JsonException)
        {
            return NotRetryable("El evento guardado no se pudo reconstruir.");
        }

        if (domainEvent is null)
        {
            return NotRetryable("El evento guardado no se pudo reconstruir.");
        }

        var refresherType = typeof(IEventRetryRefresher<>).MakeGenericType(eventType);
        var refresher = services.GetService(refresherType);
        if (refresher is not null)
        {
            var refresh = refresherType.GetMethod(nameof(IEventRetryRefresher<IDomainEvent>.RefreshAsync))!;
            var task = (Task)refresh.Invoke(refresher, [domainEvent, cancellationToken])!;
            await task;
            domainEvent = task.GetType().GetProperty("Result")!.GetValue(task) as IDomainEvent;
            if (domainEvent is null)
            {
                return NotRetryable("El dato del que depende el evento ya no existe o ya no aplica.");
            }
        }
        else if (failure.Payload.Contains(LogSanitizer.Redacted, StringComparison.Ordinal))
        {
            // Un campo sensible (p. ej. el código de MemberInvited) no se guarda, y nada sabe pedirlo de nuevo.
            return NotRetryable("El evento lleva datos que el log no guarda (códigos) y su módulo no sabe recuperarlos.");
        }

        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
        var handler = services.GetServices(handlerType)
            .FirstOrDefault(h => h is not null && h.GetType().FullName == failure.HandlerType);
        if (handler is null)
        {
            return NotRetryable("El manejador ya no existe en el sistema.");
        }

        try
        {
            var handle = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!;
            await (Task)handle.Invoke(handler, [domainEvent, cancellationToken])!;
            return new ReplayResult(EventRetryOutcomes.Succeeded, null);
        }
        catch (Exception ex)
        {
            var inner = ex is System.Reflection.TargetInvocationException { InnerException: { } i } ? i : ex;
            logger.LogWarning(inner,
                "El reintento del manejador {Handler} para el evento {Event} falló otra vez", failure.HandlerType, failure.EventType);
            return new ReplayResult(EventRetryOutcomes.Failed, LogSanitizer.Truncate(LogSanitizer.SanitizeText(inner.Message), 500));
        }
    }

    /// <summary>Solo tipos que implementan <see cref="IDomainEvent"/>: el nombre viene de una tabla, no se instancia cualquier cosa.</summary>
    private static Type? ResolveEventType(string fullName)
    {
        var type = typeof(IDomainEvent).Assembly.GetType(fullName);
        if (type is null)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(fullName, throwOnError: false);
                if (type is not null)
                {
                    break;
                }
            }
        }

        return type is not null && typeof(IDomainEvent).IsAssignableFrom(type) ? type : null;
    }

    private static ReplayResult NotRetryable(string reason) => new(EventRetryOutcomes.NotRetryable, reason);
}

/// <summary>Eventos fallidos, del más reciente al más viejo, filtrables por estado de reintento, evento y manejador.</summary>
public sealed class ListEventFailuresHandler(ILogAdminStore store)
{
    public const int MaxPageSize = 100;

    public async Task<PagedResult<EventFailureItem>> HandleAsync(EventFailuresQuery query, CancellationToken cancellationToken)
    {
        if (query.Status is { Length: > 0 } status && !EventRetryStatuses.All.Contains(status))
        {
            throw new ValidationDomainException($"Estado de reintento no válido: {status}.");
        }

        query.PageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        query.Page = Math.Max(query.Page, 1);

        var (items, total) = await store.ListEventFailuresAsync(query, cancellationToken);
        return new PagedResult<EventFailureItem>(items.Select(ToItem).ToList(), query.Page, query.PageSize, total);
    }

    internal static EventFailureItem ToItem(EventFailureRecord r) => new(
        r.Id, r.OccurredAt, r.TraceId, TraceCode.Short(r.TraceId), r.GymId, r.EventType, r.HandlerType,
        r.ExceptionType, r.Message, r.RetryStatus, r.RetriedAt, r.RetriedBy);
}

/// <summary>
/// Reintenta un evento fallido. Idempotente: si ya estaba resuelto (<c>retried_ok</c>) o ignorado no ejecuta nada.
/// Lo que no se puede reintentar queda como estaba, con la razón.
/// </summary>
public sealed class RetryEventFailureHandler(
    ILogAdminStore store,
    EventFailureReplayer replayer,
    IClock clock,
    ICurrentUserService currentUser)
{
    public async Task<RetryEventFailureResponse> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var failure = await store.GetEventFailureAsync(id, cancellationToken)
            ?? throw new NotFoundException("Evento fallido no encontrado.");

        return await RetryAsync(failure, cancellationToken);
    }

    internal async Task<RetryEventFailureResponse> RetryAsync(EventFailureRecord failure, CancellationToken cancellationToken)
    {
        if (failure.RetryStatus is EventRetryStatuses.RetriedOk or EventRetryStatuses.Ignored)
        {
            return new RetryEventFailureResponse(failure.Id, EventRetryOutcomes.AlreadyResolved, failure.RetryStatus, null);
        }

        var result = await replayer.ReplayAsync(failure, cancellationToken);
        switch (result.Outcome)
        {
            case EventRetryOutcomes.Succeeded:
                await store.MarkEventFailureAsync(
                    failure.Id, EventRetryStatuses.RetriedOk, clock.UtcNow, currentUser.UserId, cancellationToken);
                return new RetryEventFailureResponse(failure.Id, result.Outcome, EventRetryStatuses.RetriedOk, null);
            case EventRetryOutcomes.Failed:
                await store.MarkEventFailureAsync(
                    failure.Id, EventRetryStatuses.RetriedFailed, clock.UtcNow, currentUser.UserId, cancellationToken);
                return new RetryEventFailureResponse(failure.Id, result.Outcome, EventRetryStatuses.RetriedFailed, result.Message);
            default:
                return new RetryEventFailureResponse(failure.Id, result.Outcome, failure.RetryStatus, result.Message);
        }
    }
}

/// <summary>Reintenta todos los eventos reintentables de un grupo (mismo evento y mismo manejador).</summary>
public sealed class RetryEventFailureGroupHandler(ILogAdminStore store, RetryEventFailureHandler single)
{
    public const int Limit = 500;

    public async Task<RetryEventGroupResponse> HandleAsync(RetryEventGroupRequest request, CancellationToken cancellationToken)
    {
        var ids = await store.GetRetryableIdsAsync(request.EventType, request.HandlerType, Limit, cancellationToken);

        int succeeded = 0, failed = 0, notRetryable = 0;
        foreach (var id in ids)
        {
            var failure = await store.GetEventFailureAsync(id, cancellationToken);
            if (failure is null)
            {
                continue;
            }

            var result = await single.RetryAsync(failure, cancellationToken);
            switch (result.Outcome)
            {
                case EventRetryOutcomes.Succeeded:
                    succeeded++;
                    break;
                case EventRetryOutcomes.Failed:
                    failed++;
                    break;
                case EventRetryOutcomes.NotRetryable:
                    notRetryable++;
                    break;
            }
        }

        return new RetryEventGroupResponse(ids.Count, succeeded, failed, notRetryable);
    }
}

/// <summary>Descarta un evento fallido sin reintentarlo (<c>ignored</c>). Idempotente.</summary>
public sealed class IgnoreEventFailureHandler(ILogAdminStore store, IClock clock, ICurrentUserService currentUser)
{
    public async Task<RetryEventFailureResponse> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var failure = await store.GetEventFailureAsync(id, cancellationToken)
            ?? throw new NotFoundException("Evento fallido no encontrado.");

        if (failure.RetryStatus == EventRetryStatuses.RetriedOk)
        {
            throw new ConflictException("El evento ya se reintentó con éxito.");
        }

        if (failure.RetryStatus != EventRetryStatuses.Ignored)
        {
            await store.MarkEventFailureAsync(
                id, EventRetryStatuses.Ignored, clock.UtcNow, currentUser.UserId, cancellationToken);
        }

        return new RetryEventFailureResponse(id, "ignored", EventRetryStatuses.Ignored, null);
    }
}
