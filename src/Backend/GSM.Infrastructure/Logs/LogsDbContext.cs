using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Logs;

/// <summary>Fila de <c>logs.errors</c> (particionada por mes sobre <c>occurred_at</c>).</summary>
public sealed class ErrorRow : LogRowBase
{
    public string Source { get; set; } = "";
    public string? Method { get; set; }
    public string? Path { get; set; }
    public string? Endpoint { get; set; }
    public string ExceptionType { get; set; } = "";
    public string Message { get; set; } = "";
    public string? StackTrace { get; set; }
    public string? InnerExceptions { get; set; }
    public string? RequestSummary { get; set; }
    public string Fingerprint { get; set; } = "";
}

/// <summary>Fila de <c>logs.event_handler_failures</c>.</summary>
public sealed class EventHandlerFailureRow : LogRowBase
{
    public string EventType { get; set; } = "";
    public string HandlerType { get; set; } = "";
    public string Payload { get; set; } = "{}";
    public string ExceptionType { get; set; } = "";
    public string Message { get; set; } = "";
    public string? StackTrace { get; set; }
    public string RetryStatus { get; set; } = "pending";
    public DateTimeOffset? RetriedAt { get; set; }
    public Guid? RetriedBy { get; set; }
}

/// <summary>Fila de <c>logs.job_runs</c>.</summary>
public sealed class JobRunRow : LogRowBase
{
    public string Job { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset FinishedAt { get; set; }
    public string Status { get; set; } = "";
    public int ItemsProcessed { get; set; }
    public int ItemsFailed { get; set; }
    public string? Error { get; set; }
}

/// <summary>Fila de <c>logs.business_rejections</c> (particionada por mes sobre <c>occurred_at</c>).</summary>
public sealed class BusinessRejectionRow : LogRowBase
{
    public string? Method { get; set; }
    public string? Endpoint { get; set; }
    public int StatusCode { get; set; }
    public string ExceptionType { get; set; } = "";
    public string? Title { get; set; }
    public string? Detail { get; set; }
    public string? FieldErrors { get; set; }
}

/// <summary>Fila de <c>logs.security_events</c> (particionada por mes).</summary>
public sealed class SecurityEventRow : LogRowBase
{
    public string EventType { get; set; } = "";
    public string? Ip { get; set; }
    public string? UserAgent { get; set; }
    public string? Detail { get; set; }
}

/// <summary>Fila de <c>logs.slow_requests</c> (particionada por mes).</summary>
public sealed class SlowRequestRow : LogRowBase
{
    public string? Method { get; set; }
    public string? Endpoint { get; set; }
    public int DurationMs { get; set; }
    public int StatusCode { get; set; }
}

/// <summary>Fila de <c>logs.heartbeats</c>.</summary>
public sealed class HeartbeatRow
{
    public string Process { get; set; } = "";
    public DateTimeOffset LastBeatAt { get; set; }
}

/// <summary>Columnas comunes de las tablas de logs (docs/GSM-logs.md §4).</summary>
public abstract class LogRowBase
{
    public Guid Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? TraceId { get; set; }
    public Guid? GymId { get; set; }
    public Guid? UserId { get; set; }
    public string? Role { get; set; }
    public string? Environment { get; set; }
    public string? AppVersion { get; set; }
}

/// <summary>
/// DbContext propio del schema <c>logs</c>, con su propia conexión (<c>ConnectionStrings:Logs</c>, o la
/// principal si no se define). No tiene migraciones: el schema lo crea el script <c>V2_LG1.sql</c> que ejecuta
/// una migración de <c>GsmDbContext</c> (las tablas están particionadas y no se pueden describir al modelo
/// de EF). Sin Global Query Filter: nada de aquí es de un gym. Las configuraciones van aquí dentro y no como
/// <c>IEntityTypeConfiguration</c> para que <c>GsmDbContext</c> (que escanea el ensamblado) no las recoja.
/// </summary>
public sealed class LogsDbContext(DbContextOptions<LogsDbContext> options) : DbContext(options)
{
    public DbSet<ErrorRow> Errors => Set<ErrorRow>();
    public DbSet<EventHandlerFailureRow> EventHandlerFailures => Set<EventHandlerFailureRow>();
    public DbSet<JobRunRow> JobRuns => Set<JobRunRow>();
    public DbSet<BusinessRejectionRow> BusinessRejections => Set<BusinessRejectionRow>();
    public DbSet<SecurityEventRow> SecurityEvents => Set<SecurityEventRow>();
    public DbSet<SlowRequestRow> SlowRequests => Set<SlowRequestRow>();
    public DbSet<HeartbeatRow> Heartbeats => Set<HeartbeatRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("logs");

        // La PK real de `errors` es (id, occurred_at); para insertar basta con id.
        modelBuilder.Entity<ErrorRow>().ToTable("errors").HasKey(e => e.Id);

        modelBuilder.Entity<EventHandlerFailureRow>(entity =>
        {
            entity.ToTable("event_handler_failures").HasKey(e => e.Id);
            entity.Property(e => e.Payload).HasColumnType("jsonb");
        });

        // Particionadas como `errors`: la PK real es (id, occurred_at); para insertar basta con id.
        modelBuilder.Entity<BusinessRejectionRow>().ToTable("business_rejections").HasKey(e => e.Id);
        modelBuilder.Entity<SecurityEventRow>().ToTable("security_events").HasKey(e => e.Id);
        modelBuilder.Entity<SlowRequestRow>().ToTable("slow_requests").HasKey(e => e.Id);

        modelBuilder.Entity<JobRunRow>().ToTable("job_runs").HasKey(e => e.Id);

        modelBuilder.Entity<HeartbeatRow>().ToTable("heartbeats").HasKey(e => e.Process);
    }
}
