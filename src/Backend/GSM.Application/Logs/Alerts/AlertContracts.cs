namespace GSM.Application.Logs.Alerts;

/// <summary>Tipos de alerta al SuperAdmin (docs/GSM-logs.md §8). Viajan como texto.</summary>
public static class PlatformAlertKinds
{
    /// <summary>Primera ocurrencia de un grupo de <c>errors</c> o <c>event_handler_failures</c>.</summary>
    public const string NewError = "new_error";

    /// <summary>Un grupo con muchas ocurrencias en pocos minutos.</summary>
    public const string Spike = "spike";

    /// <summary>Un latido vencido: un proceso dejó de correr sin dar error.</summary>
    public const string ProcessStopped = "process_stopped";

    /// <summary>Varias fallas seguidas del mismo proveedor externo.</summary>
    public const string ExternalDown = "external_down";

    /// <summary>Ráfaga de logins fallidos desde una misma IP.</summary>
    public const string Security = "security";

    public static readonly string[] All = [NewError, Spike, ProcessStopped, ExternalDown, Security];
}

/// <summary>Estado de la entrega de una alerta por un canal (correo o push).</summary>
public static class PlatformAlertStatuses
{
    public const string Pending = "pending";
    public const string Sent = "sent";
    public const string Skipped = "skipped";
    public const string Failed = "failed";
}

/// <summary>
/// Lo que el evaluador detectó y todavía no sabe si ya avisó.
/// </summary>
/// <param name="Key">Clave de repetición: la misma clave no vuelve a avisar dentro de la ventana.</param>
/// <param name="Once"><c>true</c> = avisa una sola vez por clave, para siempre (error nuevo). <c>false</c> = cada ventana de espera.</param>
public sealed record AlertCandidate(
    string Key,
    string Kind,
    string Title,
    string Body,
    string? Link,
    bool Once);

/// <summary>Una alerta emitida, tal como la guarda <c>logs.platform_alerts</c>.</summary>
public sealed record PlatformAlertRecord(
    Guid Id,
    DateTimeOffset CreatedAt,
    string Key,
    string Kind,
    string Title,
    string Body,
    string? Link,
    string EmailStatus,
    string PushStatus,
    int Attempts,
    string? LastError);

/// <summary>Un grupo de fallas visto en una ventana reciente (errores o eventos fallidos).</summary>
/// <param name="Count">Ocurrencias dentro de la ventana consultada.</param>
public sealed record RecentFailureGroup(
    string Kind,
    string Fingerprint,
    string Title,
    string? Detail,
    int Count);

public sealed record HeartbeatInfo(string Process, DateTimeOffset LastBeatAt);

public sealed record ProviderFailureCount(string Provider, int Count);

public sealed record IpFailureCount(string Ip, int Count);

/// <summary>
/// Lo que el evaluador mide en las tablas de <c>logs</c> para decidir si hay que avisar. Lo implementa
/// Infrastructure con <c>LogsDbContext</c>.
/// </summary>
public interface IAlertSignalSource
{
    /// <summary>Grupos de <c>errors</c> y <c>event_handler_failures</c> con alguna ocurrencia desde <paramref name="since"/>.</summary>
    Task<IReadOnlyList<RecentFailureGroup>> GetFailureGroupsSinceAsync(DateTimeOffset since, CancellationToken cancellationToken);

    Task<IReadOnlyList<HeartbeatInfo>> GetHeartbeatsAsync(CancellationToken cancellationToken);

    /// <summary>Fallas de <c>external_failures</c> desde <paramref name="since"/>, por proveedor.</summary>
    Task<IReadOnlyList<ProviderFailureCount>> GetExternalFailureCountsSinceAsync(DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary><c>login_failed</c> de <c>security_events</c> desde <paramref name="since"/>, por IP (las que tienen IP).</summary>
    Task<IReadOnlyList<IpFailureCount>> GetLoginFailureCountsSinceAsync(DateTimeOffset since, CancellationToken cancellationToken);
}

/// <summary>
/// Alertas emitidas y su entrega. Lo implementa Infrastructure sobre <c>logs.alert_state</c> y
/// <c>logs.platform_alerts</c>. Las operaciones de "reclamar" son atómicas en la base: con varias copias de la
/// API evaluando a la vez, una alerta sale una sola vez y se entrega una sola vez.
/// </summary>
public interface IPlatformAlertStore
{
    /// <summary>
    /// Reclama la clave y, si la ganó, crea la alerta (pendiente de entrega) en la misma transacción.
    /// <paramref name="repeatAfter"/> es la espera mínima entre dos alertas con la misma clave; <c>null</c> = nunca repetir.
    /// </summary>
    /// <returns>El id de la alerta creada, o <c>null</c> si la clave seguía dentro de su espera.</returns>
    Task<Guid?> TryRaiseAsync(
        AlertCandidate candidate, DateTimeOffset now, TimeSpan? repeatAfter, CancellationToken cancellationToken);

    /// <summary>Alertas con alguna entrega pendiente cuya hora de reintento llegó (más antiguas primero).</summary>
    Task<IReadOnlyList<PlatformAlertRecord>> ListDueForDeliveryAsync(
        DateTimeOffset now, int take, int maxAttempts, CancellationToken cancellationToken);

    /// <summary>
    /// Reserva un intento de entrega: suma 1 a los intentos y difiere el siguiente (para que otra copia no entregue lo mismo).
    /// <c>false</c> si ya no tocaba (otra copia lo tomó, se agotaron los intentos o ya no hay nada pendiente).
    /// </summary>
    Task<bool> TryBeginDeliveryAsync(
        Guid id, DateTimeOffset now, TimeSpan holdFor, int maxAttempts, CancellationToken cancellationToken);

    /// <summary>
    /// Guarda el resultado del intento. Un canal que falló sigue <c>pending</c> mientras queden intentos; al agotarse
    /// pasa a <c>failed</c>.
    /// </summary>
    Task CompleteDeliveryAsync(
        Guid id, string emailStatus, string pushStatus, string? error, int maxAttempts, CancellationToken cancellationToken);

    Task<(IReadOnlyList<PlatformAlertRecord> Items, int Total)> ListAsync(
        int page, int pageSize, CancellationToken cancellationToken);
}

/// <summary>
/// Umbrales y plazos de las alertas (<c>Logs:Alerts</c>). Los valores por defecto son los de la especificación (§6, §8).
/// </summary>
public sealed class PlatformAlertOptions
{
    public const string SectionName = "Logs:Alerts";

    public bool Enabled { get; set; } = true;

    /// <summary>Cada cuánto evalúa el servicio en segundo plano. La tarea periódica evalúa además en cada ejecución.</summary>
    public int EvaluationIntervalSeconds { get; set; } = 60;

    /// <summary>Ventana de búsqueda de grupos nuevos: un grupo visto hace más que esto no genera "error nuevo".</summary>
    public int NewErrorLookbackMinutes { get; set; } = 15;

    public int SpikeCount { get; set; } = 20;

    public int SpikeWindowMinutes { get; set; } = 10;

    public int ExternalFailureCount { get; set; } = 5;

    public int ExternalWindowMinutes { get; set; } = 10;

    public int LoginFailureCount { get; set; } = 20;

    public int LoginWindowMinutes { get; set; } = 10;

    /// <summary>"No repetir la misma alerta más de una vez cada 30 minutos".</summary>
    public int RepeatMinutes { get; set; } = 30;

    /// <summary>Plazo del latido del dispatcher de avisos.</summary>
    public int DispatcherHeartbeatMinutes { get; set; } = 5;

    /// <summary>Plazo del latido de la tarea periódica (<c>*_job</c>).</summary>
    public int JobHeartbeatMinutes { get; set; } = 30;

    public int MaxDeliveryAttempts { get; set; } = 3;
}

public sealed record PlatformAlertItem(
    Guid Id,
    DateTimeOffset CreatedAt,
    string Kind,
    string Title,
    string Body,
    string? Link,
    string EmailStatus,
    string PushStatus);
