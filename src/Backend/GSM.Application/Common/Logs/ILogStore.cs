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

    /// <summary>Una llamada fallida a un servicio externo (Resend, R2, Firebase).</summary>
    void RecordExternalFailure(ExternalFailureLogEntry entry);

    /// <summary>Un error que reportó el navegador o la app (<c>POST /api/logs/client</c>).</summary>
    void RecordClientError(ClientErrorLogEntry entry);

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

/// <param name="Provider">Ver <see cref="ExternalProviders"/>.</param>
/// <param name="Operation">Ver <see cref="ExternalOperations"/>.</param>
/// <param name="ResponseBody">Respuesta del proveedor; la implementación la limpia (§3) y la recorta.</param>
/// <param name="Attempt">Número de intento (1 si no hay reintentos propios).</param>
public sealed record ExternalFailureLogEntry(
    string Provider,
    string Operation,
    int? StatusCode,
    string? ResponseBody,
    int DurationMs,
    int Attempt = 1);

/// <summary>Plataformas válidas de <c>logs.client_errors</c> (docs/GSM-logs.md §4.6).</summary>
public static class ClientPlatforms
{
    public const string WebPanel = "web_panel";
    public const string WebMember = "web_member";
    public const string Android = "android";
    public const string Ios = "ios";

    public static readonly string[] All = [WebPanel, WebMember, Android, Ios];
}

/// <param name="TraceId">El código de rastreo de la petición que falló, si el cliente lo tenía; si no, el de este envío.</param>
/// <param name="Route">Pantalla donde pasó; la implementación le quita la consulta (puede llevar códigos).</param>
/// <param name="AppVersion">Versión del cliente; si es <c>null</c> se usa la de la API.</param>
public sealed record ClientErrorLogEntry(
    string Platform,
    string? Route,
    string ExceptionType,
    string Message,
    string? StackTrace,
    string? DeviceInfo,
    string? TraceId = null,
    string? AppVersion = null);

public static class ExternalProviders
{
    public const string Resend = "resend";
    public const string R2 = "r2";
    public const string Fcm = "fcm";
}

public static class ExternalOperations
{
    public const string SendEmail = "send_email";
    public const string Upload = "upload";
    public const string Delete = "delete";
    public const string Send = "send";
    public const string Auth = "auth";
}

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
