using System.Collections.Concurrent;
using GSM.Application.Common.Events;
using GSM.Application.Routines.Events;

namespace GSM.IntegrationTests;

/// <summary>
/// Captura las publicaciones de <see cref="RoutineSessionLogged"/> y <see cref="PersonalRecordSet"/> para los tests de
/// idempotencia.
/// </summary>
public sealed class RoutineEventRecorder
    : IDomainEventHandler<RoutineSessionLogged>, IDomainEventHandler<PersonalRecordSet>
{
    private readonly ConcurrentQueue<RoutineSessionLogged> _sessions = new();
    private readonly ConcurrentQueue<PersonalRecordSet> _records = new();

    public IReadOnlyList<RoutineSessionLogged> Sessions => _sessions.ToArray();

    public IReadOnlyList<PersonalRecordSet> Records => _records.ToArray();

    public void Clear()
    {
        _sessions.Clear();
        _records.Clear();
    }

    public Task HandleAsync(RoutineSessionLogged domainEvent, CancellationToken cancellationToken = default)
    {
        _sessions.Enqueue(domainEvent);
        return Task.CompletedTask;
    }

    public Task HandleAsync(PersonalRecordSet domainEvent, CancellationToken cancellationToken = default)
    {
        _records.Enqueue(domainEvent);
        return Task.CompletedTask;
    }
}
