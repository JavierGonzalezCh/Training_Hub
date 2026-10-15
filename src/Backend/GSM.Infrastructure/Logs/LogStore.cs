using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Logs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GSM.Infrastructure.Logs;

/// <summary>
/// <see cref="ILogStore"/>: encola en memoria y un escritor en segundo plano guarda en bloque (cada segundo o
/// cada 100 registros) con <see cref="LogsDbContext"/>, fuera de la transacción de cualquier caso de uso.
/// Si escribir falla, los registros caen al log de Serilog; nunca se propaga una excepción al llamador.
/// </summary>
public sealed class LogStore : BackgroundService, ILogStore
{
    public const int BatchSize = 100;
    public static readonly TimeSpan BatchInterval = TimeSpan.FromSeconds(1);
    private const int QueueCapacity = 10_000;
    private const int MessageMax = 8_000;
    private const int StackTraceMax = 32_000;
    private const int PayloadMax = 65_536;
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(30);

    private static readonly string Version = ResolveVersion();

    private static readonly JsonSerializerOptions FallbackJson = new(JsonSerializerDefaults.Web);

    private readonly Channel<object> _queue = Channel.CreateBounded<object>(
        new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

    private readonly IDbContextFactory<LogsDbContext> _contextFactory;
    private readonly IClock _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly ITraceContext _trace;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<LogStore> _logger;
    private readonly IClientInfo? _clientInfo;

    public LogStore(
        IDbContextFactory<LogsDbContext> contextFactory,
        IClock clock,
        ICurrentUserService currentUser,
        ITraceContext trace,
        IHostEnvironment environment,
        ILogger<LogStore> logger,
        IClientInfo? clientInfo = null)
    {
        _clientInfo = clientInfo;
        _contextFactory = contextFactory;
        _clock = clock;
        _currentUser = currentUser;
        _trace = trace;
        _environment = environment;
        _logger = logger;
    }

    // ---------------------------------------------------------------- ILogStore

    public void RecordError(ErrorLogEntry entry)
    {
        Guard(() =>
        {
            var exception = ErrorFingerprint.Unwrap(entry.Exception);
            var row = new ErrorRow
            {
                Source = entry.Source.ToString().ToLowerInvariant(),
                Method = Clip(entry.Method, 16),
                Path = Clip(entry.Path, 512),
                Endpoint = Clip(entry.Endpoint, 512),
                ExceptionType = Clip(exception.GetType().FullName ?? exception.GetType().Name, 256)!,
                Message = Clip(exception.Message, MessageMax)!,
                StackTrace = Clip(exception.StackTrace, StackTraceMax),
                InnerExceptions = Clip(DescribeInner(exception), StackTraceMax),
                RequestSummary = entry.RequestSummary is null
                    ? null
                    : LogSanitizer.Truncate(entry.RequestSummary, LogSanitizer.RequestSummaryMaxChars),
                Fingerprint = ErrorFingerprint.Compute(exception),
            };
            Stamp(row);
            Enqueue(row);
        });
    }

    public void RecordEventHandlerFailure(EventHandlerFailureLogEntry entry)
    {
        Guard(() =>
        {
            var exception = ErrorFingerprint.Unwrap(entry.Exception);
            var payload = LogSanitizer.SanitizeJson(entry.PayloadJson);
            if (payload.Length > PayloadMax)
            {
                // Un JSON recortado no se puede reintentar ni cabe en jsonb: se deja constancia de que se omitió.
                payload = "{\"_truncated\":true}";
            }

            var row = new EventHandlerFailureRow
            {
                EventType = Clip(entry.EventType, 512)!,
                HandlerType = Clip(entry.HandlerType, 512)!,
                Payload = payload,
                ExceptionType = Clip(exception.GetType().FullName ?? exception.GetType().Name, 256)!,
                Message = Clip(exception.Message, MessageMax)!,
                StackTrace = Clip(exception.StackTrace, StackTraceMax),
                RetryStatus = "pending",
            };
            Stamp(row);
            Enqueue(row);
        });
    }

    public void RecordJobRun(JobRunLogEntry entry)
    {
        Guard(() =>
        {
            var row = new JobRunRow
            {
                Job = Clip(entry.Job, 64)!,
                StartedAt = entry.StartedAt,
                FinishedAt = entry.FinishedAt,
                Status = entry.Status.ToString().ToLowerInvariant(),
                ItemsProcessed = entry.ItemsProcessed,
                ItemsFailed = entry.ItemsFailed,
                Error = Clip(entry.Error, MessageMax),
            };
            Stamp(row);
            row.OccurredAt = entry.StartedAt;
            Enqueue(row);
        });
    }

    public void RecordBusinessRejection(BusinessRejectionLogEntry entry)
    {
        Guard(() =>
        {
            var row = new BusinessRejectionRow
            {
                Method = Clip(entry.Method, 16),
                Endpoint = Clip(entry.Endpoint, 512),
                StatusCode = entry.StatusCode,
                ExceptionType = Clip(entry.ExceptionType, 256)!,
                Title = Clip(entry.Title, 512),
                Detail = Clip(entry.Detail, MessageMax),
                FieldErrors = Clip(entry.FieldErrors, MessageMax),
            };
            Stamp(row);
            Enqueue(row);
        });
    }

    public void RecordSecurityEvent(SecurityEventLogEntry entry)
    {
        Guard(() =>
        {
            var row = new SecurityEventRow
            {
                EventType = Clip(entry.EventType, 64)!,
                Ip = Clip(entry.Ip ?? _clientInfo?.Ip, 64),
                UserAgent = Clip(entry.UserAgent ?? _clientInfo?.UserAgent, 512),
                Detail = Clip(entry.Detail, MessageMax),
            };
            Stamp(row);
            Enqueue(row);
        });
    }

    public void RecordSlowRequest(SlowRequestLogEntry entry)
    {
        Guard(() =>
        {
            var row = new SlowRequestRow
            {
                Method = Clip(entry.Method, 16),
                Endpoint = Clip(entry.Endpoint, 512),
                DurationMs = entry.DurationMs,
                StatusCode = entry.StatusCode,
            };
            Stamp(row);
            Enqueue(row);
        });
    }

    public void RecordHeartbeat(string process, DateTimeOffset at) =>
        Guard(() => Enqueue(new HeartbeatRow { Process = Clip(process, 64)!, LastBeatAt = at }));

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        var marker = new FlushMarker();
        try
        {
            await _queue.Writer.WriteAsync(marker, cancellationToken);
            await marker.Done.Task.WaitAsync(FlushTimeout, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "No se pudo vaciar la cola de logs");
        }
    }

    // ---------------------------------------------------------------- escritor en segundo plano

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = _queue.Reader;
        var batch = new List<object>(BatchSize);
        var flushes = new List<FlushMarker>();

        try
        {
            while (await reader.WaitToReadAsync(stoppingToken))
            {
                var deadline = DateTime.UtcNow + BatchInterval;

                while (batch.Count < BatchSize)
                {
                    if (reader.TryRead(out var item))
                    {
                        if (item is FlushMarker marker)
                        {
                            flushes.Add(marker);
                            break;
                        }

                        batch.Add(item);
                        continue;
                    }

                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                    {
                        break;
                    }

                    using var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    wait.CancelAfter(remaining);
                    try
                    {
                        if (!await reader.WaitToReadAsync(wait.Token))
                        {
                            break;
                        }
                    }
                    catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                    {
                        break;   // venció el segundo de espera
                    }
                }

                await WriteBatchAsync(batch);
                batch.Clear();
                CompleteAll(flushes);
            }
        }
        catch (OperationCanceledException)
        {
            // apagado: se vacía lo pendiente en StopAsync.
        }
        finally
        {
            while (_queue.Reader.TryRead(out var item))
            {
                if (item is FlushMarker marker)
                {
                    flushes.Add(marker);
                }
                else
                {
                    batch.Add(item);
                }
            }

            await WriteBatchAsync(batch);
            CompleteAll(flushes);
        }
    }

    private async Task WriteBatchAsync(List<object> batch)
    {
        if (batch.Count == 0)
        {
            return;
        }

        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync();
            foreach (var item in batch)
            {
                switch (item)
                {
                    case ErrorRow row: db.Errors.Add(row); break;
                    case EventHandlerFailureRow row: db.EventHandlerFailures.Add(row); break;
                    case JobRunRow row: db.JobRuns.Add(row); break;
                    case BusinessRejectionRow row: db.BusinessRejections.Add(row); break;
                    case SecurityEventRow row: db.SecurityEvents.Add(row); break;
                    case SlowRequestRow row: db.SlowRequests.Add(row); break;
                }
            }

            await db.SaveChangesAsync();

            // Upsert del latido (último valor por proceso del lote).
            foreach (var beat in batch.OfType<HeartbeatRow>().GroupBy(h => h.Process).Select(g => g.Last()))
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     INSERT INTO logs.heartbeats (process, last_beat_at) VALUES ({beat.Process}, {beat.LastBeatAt})
                     ON CONFLICT (process) DO UPDATE SET last_beat_at = EXCLUDED.last_beat_at
                     """);
            }
        }
        catch (Exception ex)
        {
            // La base de logs no responde (o rechazó el lote): al log de archivo, y la petición ni se entera.
            _logger.LogError(ex, "No se pudieron guardar {Count} registros de logs; se vuelcan al log de archivo", batch.Count);
            foreach (var item in batch)
            {
                try
                {
                    _logger.LogError("Registro de log no guardado ({Kind}): {Row}",
                        item.GetType().Name, JsonSerializer.Serialize(item, item.GetType(), FallbackJson));
                }
                catch
                {
                    // nada más que hacer
                }
            }
        }
    }

    private static void CompleteAll(List<FlushMarker> flushes)
    {
        foreach (var marker in flushes)
        {
            marker.Done.TrySetResult();
        }

        flushes.Clear();
    }

    // ---------------------------------------------------------------- utilidades

    private void Enqueue(object row)
    {
        if (!_queue.Writer.TryWrite(row))
        {
            _logger.LogWarning("La cola de logs está llena; se descarta un registro {Kind}", row.GetType().Name);
        }
    }

    /// <summary>Completa las columnas comunes en el momento de encolar (después ya no hay petición).</summary>
    private void Stamp(LogRowBase row)
    {
        row.Id = Guid.CreateVersion7();
        row.OccurredAt = _clock.UtcNow;
        row.TraceId = Clip(_trace.TraceId, 64);
        row.GymId = _currentUser.GymId;
        row.UserId = _currentUser.UserId;
        row.Role = _currentUser.Role?.ToString().ToLowerInvariant();
        row.Environment = Clip(_environment.EnvironmentName, 32);
        row.AppVersion = Version;
    }

    /// <summary>Nada de lo que hace el log puede propagarse a quien lo invoca.</summary>
    private void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo encolar un registro de log");
        }
    }

    private static string? Clip(string? text, int max) =>
        text is null ? null : text.Length <= max ? text : text[..max];

    private static string? DescribeInner(Exception exception)
    {
        if (exception.InnerException is null)
        {
            return null;
        }

        var text = new StringBuilder();
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            text.Append(inner.GetType().FullName).Append(": ").AppendLine(inner.Message);
        }

        return text.ToString();
    }

    private static string ResolveVersion()
    {
        var info = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(LogStore).Assembly.GetName().Version?.ToString()
            ?? "unknown";
        var plus = info.IndexOf('+');
        return plus > 0 ? info[..plus] : info;
    }

    private sealed class FlushMarker
    {
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
