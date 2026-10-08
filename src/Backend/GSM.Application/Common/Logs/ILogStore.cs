namespace GSM.Application.Common.Logs;

/// <summary>
/// Servicio transversal de logs consultables (schema <c>logs</c>, ver <c>docs/GSM-logs.md</c>). Un
/// método por tabla. Escribir **nunca** espera ni lanza: el registro se encola en memoria y un escritor
/// en segundo plano lo guarda en bloque con su propio DbContext (fuera de la transacción del caso de uso).
/// Los campos comunes (TraceId, gym, usuario, rol, entorno, versión) los completa la implementación en el
/// momento de encolar.
/// </summary>
public interface ILogStore
{
    /// <summary>Un error no controlado (500, o excepción fuera de una petición).</summary>
    void RecordError(ErrorLogEntry entry);

    /// <summary>Un manejador de evento de dominio que falló; guarda el evento para poder reintentarlo.</summary>
    void RecordEventHandlerFailure(EventHandlerFailureLogEntry entry);

    /// <summary>Una ejecución de una tarea periódica.</summary>
    void RecordJobRun(JobRunLogEntry entry);

    /// <summary>Marca que un proceso sigue vivo (upsert por nombre de proceso).</summary>
    void RecordHeartbeat(string process, DateTimeOffset at);

    /// <summary>Espera a que todo lo encolado hasta ahora se haya intentado escribir. Para tests y apagado.</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}

public enum ErrorSource
{
    Api,
    Dispatcher,
    Job,
}

/// <param name="Exception">La excepción; la implementación extrae tipo, mensaje, traza, internas y huella.</param>
/// <param name="RequestSummary">Ruta/consulta/cuerpo en crudo; se limpian (§3) y recortan antes de guardar.</param>
public sealed record ErrorLogEntry(
    ErrorSource Source,
    Exception Exception,
    string? Method = null,
    string? Path = null,
    string? Endpoint = null,
    string? RequestSummary = null);

/// <param name="EventType">Nombre completo del tipo del evento (para resolverlo al reintentar).</param>
/// <param name="HandlerType">Nombre completo del tipo del manejador.</param>
/// <param name="PayloadJson">El evento serializado; se limpia (§3) antes de guardar.</param>
public sealed record EventHandlerFailureLogEntry(
    string EventType,
    string HandlerType,
    string PayloadJson,
    Exception Exception);

public sealed record JobRunLogEntry(
    string Job,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    JobRunStatus Status,
    int ItemsProcessed,
    int ItemsFailed,
    string? Error);

public enum JobRunStatus
{
    Ok,
    Partial,
    Failed,
}
