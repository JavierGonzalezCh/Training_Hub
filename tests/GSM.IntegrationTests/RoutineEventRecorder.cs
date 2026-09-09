using System.Collections.Concurrent;
using GSM.Application.Common.Events;
using GSM.Application.Routines.Events;

namespace GSM.IntegrationTests;

/// <summary>Captura las publicaciones de <see cref="RoutineSessionLogged"/> para los tests de idempotencia.</summary>
public sealed class RoutineEventRecorder : IDomainEventHandler<RoutineSessionLogged>
{
    private readonly ConcurrentQueue<RoutineSessionLogged> _sessions = new();

    public IReadOnlyList<RoutineSessionLogged> Sessions => _sessions.ToArray();

    public void Clear() => _sessions.Clear();

    public Task HandleAsync(RoutineSessionLogged domainEvent, CancellationToken cancellationToken = default)
    {
        _sessions.Enqueue(domainEvent);
        return Task.CompletedTask;
    }
}
