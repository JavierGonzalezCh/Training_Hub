namespace GSM.Application.Logs;

/// <summary>Tipos de grupo del panel de fallas (docs/GSM-logs.md §7). Viajan como texto.</summary>
public static class ErrorGroupKinds
{
    /// <summary><c>logs.errors</c>: agrupa por huella.</summary>
    public const string Error = "error";

    /// <summary><c>logs.event_handler_failures</c>: agrupa por evento + manejador.</summary>
    public const string EventFailure = "event_failure";

    /// <summary><c>logs.external_failures</c>: agrupa por proveedor + operación + código.</summary>
    public const string External = "external";

    /// <summary><c>logs.client_errors</c>: agrupa por plataforma + tipo de excepción.</summary>
    public const string Client = "client";

    public static readonly string[] All = [Error, EventFailure, External, Client];
}

/// <summary>Estados de un grupo (<c>logs.error_groups</c>). Sin fila, un grupo es <c>new</c>.</summary>
public static class ErrorGroupStatuses
{
    public const string New = "new";
    public const string Investigating = "investigating";
    public const string Resolved = "resolved";
    public const string Ignored = "ignored";

    public static readonly string[] All = [New, Investigating, Resolved, Ignored];
}

/// <summary>Estados de reintento de un evento fallido (<c>logs.event_handler_failures.retry_status</c>).</summary>
public static class EventRetryStatuses
{
    public const string Pending = "pending";
    public const string RetriedOk = "retried_ok";
    public const string RetriedFailed = "retried_failed";
    public const string Ignored = "ignored";

    public static readonly string[] All = [Pending, RetriedOk, RetriedFailed, Ignored];
}

/// <summary>Resultado de reintentar un evento fallido.</summary>
public static class EventRetryOutcomes
{
    /// <summary>El manejador volvió a correr y terminó bien.</summary>
    public const string Succeeded = "succeeded";

    /// <summary>El manejador volvió a correr y falló otra vez.</summary>
    public const string Failed = "failed";

    /// <summary>El evento ya estaba resuelto o ignorado: no se ejecutó nada (idempotente).</summary>
    public const string AlreadyResolved = "already_resolved";

    /// <summary>No se puede reintentar (datos que no se guardan, evento truncado, tipo desconocido...).</summary>
    public const string NotRetryable = "not_retryable";
}

// ---- Lo que devuelve la API ----

/// <param name="Kind">Ver <see cref="ErrorGroupKinds"/>.</param>
/// <param name="Fingerprint">Clave del grupo dentro de su tipo.</param>
/// <param name="Title">Qué es (tipo de excepción, evento y manejador, proveedor...).</param>
/// <param name="Detail">Dónde (endpoint, pantalla, operación...), si aplica.</param>
/// <param name="Count">Cantidad de ocurrencias guardadas (dentro de la retención).</param>
/// <param name="GymsAffected">Gyms distintos entre las ocurrencias.</param>
/// <param name="Status">Estado efectivo (ver <see cref="ErrorGroupStatuses"/>); un resuelto que volvió a ocurrir sale <c>new</c>.</param>
/// <param name="Reopened"><c>true</c> si el grupo estaba resuelto y volvió a ocurrir.</param>
/// <param name="PendingRetries">Solo en eventos fallidos: cuántas ocurrencias se pueden reintentar todavía.</param>
public sealed record ErrorGroupItem(
    string Kind,
    string Fingerprint,
    string Title,
    string? Detail,
    int Count,
    int GymsAffected,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    string Status,
    bool Reopened,
    string? Notes,
    int PendingRetries);

public sealed record ErrorOccurrenceItem(
    Guid Id,
    DateTimeOffset OccurredAt,
    string? TraceId,
    string? TraceCode,
    Guid? GymId,
    Guid? UserId,
    string? Role,
    string? Method,
    string? Path,
    string Message,
    string? StackTrace,
    string? RequestSummary,
    string? RetryStatus);

public sealed record ErrorGroupDetail(
    ErrorGroupItem Group,
    IReadOnlyList<ErrorOccurrenceItem> Occurrences);

/// <summary>Un registro cualquiera de <c>logs</c> que comparte el código de rastreo buscado.</summary>
/// <param name="Source">Tabla de origen: errors, event_handler_failures, business_rejections...</param>
public sealed record TraceRecordItem(
    string Source,
    Guid Id,
    DateTimeOffset OccurredAt,
    string TraceId,
    string TraceCode,
    Guid? GymId,
    Guid? UserId,
    string? Role,
    string Summary,
    string? Detail,
    int? StatusCode);

public sealed record TraceSearchResponse(
    string Query,
    IReadOnlyList<TraceRecordItem> Records);

public sealed record EventFailureItem(
    Guid Id,
    DateTimeOffset OccurredAt,
    string? TraceId,
    string? TraceCode,
    Guid? GymId,
    string EventType,
    string HandlerType,
    string ExceptionType,
    string Message,
    string RetryStatus,
    DateTimeOffset? RetriedAt,
    Guid? RetriedBy);

public sealed record RetryEventFailureResponse(
    Guid Id,
    string Outcome,
    string RetryStatus,
    string? Message);

public sealed record RetryEventGroupResponse(
    int Total,
    int Succeeded,
    int Failed,
    int NotRetryable);

// ---- Peticiones ----

public sealed class ErrorGroupsQuery
{
    public string? Kind { get; set; }

    public string? Status { get; set; }

    /// <summary><c>recent</c> (última vez, por defecto), <c>frequent</c> (más ocurrencias) o <c>new</c> (primera vez más reciente).</summary>
    public string? Sort { get; set; }

    public string? Search { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 25;
}

public sealed record SetErrorGroupStatusRequest(
    string Kind,
    string Fingerprint,
    string Status,
    string? Notes);

public sealed class EventFailuresQuery
{
    /// <summary>Estado de reintento; vacío = todos.</summary>
    public string? Status { get; set; }

    public string? EventType { get; set; }

    public string? HandlerType { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 25;
}

public sealed record RetryEventGroupRequest(string EventType, string HandlerType);
