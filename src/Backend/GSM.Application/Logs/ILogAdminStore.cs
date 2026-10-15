namespace GSM.Application.Logs;

/// <summary>Un grupo agregado desde las tablas de <c>logs</c>, sin su estado.</summary>
public sealed record LogGroupAggregate(
    string Kind,
    string Fingerprint,
    string Title,
    string? Detail,
    int Count,
    int GymsAffected,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    int PendingRetries);

/// <summary>Fila de <c>logs.error_groups</c>: el estado y las notas que dejó el SuperAdmin.</summary>
public sealed record LogGroupState(
    string Kind,
    string Fingerprint,
    string Title,
    DateTimeOffset FirstSeenAt,
    string Status,
    string? Notes,
    DateTimeOffset StatusChangedAt,
    Guid? StatusChangedBy);

/// <summary>Un evento fallido completo, con su payload, para reintentarlo.</summary>
public sealed record EventFailureRecord(
    Guid Id,
    DateTimeOffset OccurredAt,
    string? TraceId,
    Guid? GymId,
    string EventType,
    string HandlerType,
    string Payload,
    string ExceptionType,
    string Message,
    string RetryStatus,
    DateTimeOffset? RetriedAt,
    Guid? RetriedBy);

/// <summary>
/// Lectura (y la escritura mínima del panel) del schema <c>logs</c> para el SuperAdmin. Lo implementa
/// Infrastructure con <c>LogsDbContext</c>; nada de aquí pasa por el Global Query Filter.
/// </summary>
public interface ILogAdminStore
{
    /// <summary>Los grupos más recientes de cada tipo (o solo de <paramref name="kind"/>), con un tope por tipo.</summary>
    Task<IReadOnlyList<LogGroupAggregate>> GetGroupsAsync(string? kind, int limitPerKind, CancellationToken cancellationToken);

    Task<LogGroupAggregate?> GetGroupAsync(string kind, string fingerprint, CancellationToken cancellationToken);

    Task<IReadOnlyList<ErrorOccurrenceItem>> GetOccurrencesAsync(
        string kind, string fingerprint, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<LogGroupState>> GetGroupStatesAsync(CancellationToken cancellationToken);

    Task<LogGroupState?> GetGroupStateAsync(string kind, string fingerprint, CancellationToken cancellationToken);

    /// <summary>Crea o reemplaza el estado del grupo.</summary>
    Task SaveGroupStateAsync(LogGroupState state, CancellationToken cancellationToken);

    /// <summary>Registros de todas las tablas cuyo código de rastreo empieza por <paramref name="traceIdPrefix"/> (minúsculas).</summary>
    Task<IReadOnlyList<TraceRecordItem>> SearchTraceAsync(string traceIdPrefix, int limit, CancellationToken cancellationToken);

    Task<(IReadOnlyList<EventFailureRecord> Items, int Total)> ListEventFailuresAsync(
        EventFailuresQuery query, CancellationToken cancellationToken);

    Task<EventFailureRecord?> GetEventFailureAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Ids de las ocurrencias de un evento+manejador que se pueden reintentar (<c>pending</c> o <c>retried_failed</c>).</summary>
    Task<IReadOnlyList<Guid>> GetRetryableIdsAsync(
        string eventType, string handlerType, int limit, CancellationToken cancellationToken);

    /// <summary>Cambia el estado de reintento; devuelve <c>false</c> si el id no existe.</summary>
    Task<bool> MarkEventFailureAsync(
        Guid id, string retryStatus, DateTimeOffset at, Guid? by, CancellationToken cancellationToken);
}
