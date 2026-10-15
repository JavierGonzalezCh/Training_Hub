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

    /// <summary>Un rechazo de negocio (400, 403, 404, 409 o 422): las reglas funcionando, no fallas del sistema.</summary>
    void RecordBusinessRejection(BusinessRejectionLogEntry entry);

    /// <summary>Un evento de seguridad (login fallido, bloqueo, token inválido, acceso denegado...).</summary>
    void RecordSecurityEvent(SecurityEventLogEntry entry);

    /// <summary>Una petición que tardó más que <c>Logs:SlowRequestMs</c>.</summary>
    void RecordSlowRequest(SlowRequestLogEntry entry);

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

/// <param name="ExceptionType">Tipo de la excepción de dominio, o "validation" (400 de formulario) / "http_status".</param>
/// <param name="FieldErrors">Solo en 400: JSON con los nombres de los campos que fallaron (nunca los valores).</param>
public sealed record BusinessRejectionLogEntry(
    string? Method,
    string? Endpoint,
    int StatusCode,
    string ExceptionType,
    string? Title,
    string? Detail,
    string? FieldErrors = null);

/// <param name="Ip">Si es <c>null</c> la implementación usa la de la petición en curso.</param>
/// <param name="UserAgent">Si es <c>null</c> la implementación usa el de la petición en curso.</param>
/// <param name="Detail">Texto libre sin contraseñas, tokens ni códigos (p. ej. el email intentado).</param>
public sealed record SecurityEventLogEntry(
    string EventType,
    string? Detail = null,
    string? Ip = null,
    string? UserAgent = null);

public sealed record SlowRequestLogEntry(
    string? Method,
    string? Endpoint,
    int DurationMs,
    int StatusCode);

/// <summary>Nombres de los eventos de <c>logs.security_events</c> (docs/GSM-logs.md §4.7).</summary>
public static class SecurityEventTypes
{
    public const string LoginFailed = "login_failed";
    public const string AccountLocked = "account_locked";
    public const string InvalidToken = "invalid_token";
    public const string PermissionDenied = "permission_denied";
    public const string RateLimited = "rate_limited";
    public const string KioskPairingFailed = "kiosk_pairing_failed";
    public const string DocumentEntryLocked = "document_entry_locked";
    public const string CodeAttemptsExceeded = "code_attempts_exceeded";
}
