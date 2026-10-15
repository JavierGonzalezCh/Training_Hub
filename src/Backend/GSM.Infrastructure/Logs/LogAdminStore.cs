using System.Linq.Expressions;
using GSM.Application.Common.Logs;
using GSM.Application.Logs;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Logs;

/// <summary><see cref="ILogAdminStore"/> sobre <see cref="LogsDbContext"/>: lectura del panel de errores del SuperAdmin.</summary>
public sealed class LogAdminStore(IDbContextFactory<LogsDbContext> contextFactory) : ILogAdminStore
{
    private const char Sep = '|';

    // ------------------------------------------------------------------ grupos

    public async Task<IReadOnlyList<LogGroupAggregate>> GetGroupsAsync(
        string? kind, int limitPerKind, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var result = new List<LogGroupAggregate>();

        if (kind is null or ErrorGroupKinds.Error)
        {
            result.AddRange(await ErrorGroupsAsync(db, null, limitPerKind, cancellationToken));
        }

        if (kind is null or ErrorGroupKinds.EventFailure)
        {
            result.AddRange(await EventGroupsAsync(db, null, limitPerKind, cancellationToken));
        }

        if (kind is null or ErrorGroupKinds.External)
        {
            result.AddRange(await ExternalGroupsAsync(db, null, limitPerKind, cancellationToken));
        }

        if (kind is null or ErrorGroupKinds.Client)
        {
            result.AddRange(await ClientGroupsAsync(db, null, limitPerKind, cancellationToken));
        }

        return result;
    }

    public async Task<LogGroupAggregate?> GetGroupAsync(string kind, string fingerprint, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var parts = fingerprint.Split(Sep);

        switch (kind)
        {
            case ErrorGroupKinds.Error:
                return (await ErrorGroupsAsync(db, e => e.Fingerprint == fingerprint, 1, cancellationToken)).FirstOrDefault();
            case ErrorGroupKinds.EventFailure when parts.Length == 2:
                return (await EventGroupsAsync(
                    db, e => e.EventType == parts[0] && e.HandlerType == parts[1], 1, cancellationToken)).FirstOrDefault();
            case ErrorGroupKinds.External when parts.Length == 3:
                var status = int.TryParse(parts[2], out var code) ? (int?)code : null;
                return (await ExternalGroupsAsync(
                    db, e => e.Provider == parts[0] && e.Operation == parts[1] && e.StatusCode == status,
                    1, cancellationToken)).FirstOrDefault();
            case ErrorGroupKinds.Client when parts.Length == 2:
                return (await ClientGroupsAsync(
                    db, e => e.Platform == parts[0] && e.ExceptionType == parts[1], 1, cancellationToken)).FirstOrDefault();
            default:
                return null;
        }
    }

    private static async Task<List<LogGroupAggregate>> ErrorGroupsAsync(
        LogsDbContext db, Expression<Func<ErrorRow, bool>>? where, int limit, CancellationToken ct)
    {
        var source = db.Errors.AsNoTracking();
        if (where is not null)
        {
            source = source.Where(where);
        }

        var rows = await source
            .GroupBy(e => e.Fingerprint)
            .Select(g => new
            {
                Fingerprint = g.Key,
                Type = g.Max(e => e.ExceptionType)!,
                Endpoint = g.Max(e => e.Endpoint),
                Count = g.Count(),
                Gyms = g.Select(e => e.GymId).Distinct().Count(),
                First = g.Min(e => e.OccurredAt),
                Last = g.Max(e => e.OccurredAt),
            })
            .OrderByDescending(x => x.Last)
            .Take(limit)
            .ToListAsync(ct);

        return rows.Select(r => new LogGroupAggregate(
            ErrorGroupKinds.Error, r.Fingerprint, r.Type, r.Endpoint, r.Count, r.Gyms, r.First, r.Last, 0)).ToList();
    }

    private static async Task<List<LogGroupAggregate>> EventGroupsAsync(
        LogsDbContext db, Expression<Func<EventHandlerFailureRow, bool>>? where, int limit, CancellationToken ct)
    {
        var source = db.EventHandlerFailures.AsNoTracking();
        if (where is not null)
        {
            source = source.Where(where);
        }

        var rows = await source
            .GroupBy(e => new { e.EventType, e.HandlerType })
            .Select(g => new
            {
                g.Key.EventType,
                g.Key.HandlerType,
                Count = g.Count(),
                Gyms = g.Select(e => e.GymId).Distinct().Count(),
                First = g.Min(e => e.OccurredAt),
                Last = g.Max(e => e.OccurredAt),
                Pending = g.Count(e => e.RetryStatus == "pending" || e.RetryStatus == "retried_failed"),
            })
            .OrderByDescending(x => x.Last)
            .Take(limit)
            .ToListAsync(ct);

        return rows.Select(r => new LogGroupAggregate(
            ErrorGroupKinds.EventFailure, r.EventType + Sep + r.HandlerType,
            $"{Short(r.EventType)} → {Short(r.HandlerType)}", null,
            r.Count, r.Gyms, r.First, r.Last, r.Pending)).ToList();
    }

    private static async Task<List<LogGroupAggregate>> ExternalGroupsAsync(
        LogsDbContext db, Expression<Func<ExternalFailureRow, bool>>? where, int limit, CancellationToken ct)
    {
        var source = db.ExternalFailures.AsNoTracking();
        if (where is not null)
        {
            source = source.Where(where);
        }

        var rows = await source
            .GroupBy(e => new { e.Provider, e.Operation, e.StatusCode })
            .Select(g => new
            {
                g.Key.Provider,
                g.Key.Operation,
                g.Key.StatusCode,
                Count = g.Count(),
                Gyms = g.Select(e => e.GymId).Distinct().Count(),
                First = g.Min(e => e.OccurredAt),
                Last = g.Max(e => e.OccurredAt),
            })
            .OrderByDescending(x => x.Last)
            .Take(limit)
            .ToListAsync(ct);

        return rows.Select(r => new LogGroupAggregate(
            ErrorGroupKinds.External, $"{r.Provider}{Sep}{r.Operation}{Sep}{r.StatusCode?.ToString() ?? "-"}",
            $"{r.Provider} · {r.Operation}", r.StatusCode is { } c ? $"HTTP {c}" : "sin respuesta",
            r.Count, r.Gyms, r.First, r.Last, 0)).ToList();
    }

    private static async Task<List<LogGroupAggregate>> ClientGroupsAsync(
        LogsDbContext db, Expression<Func<ClientErrorRow, bool>>? where, int limit, CancellationToken ct)
    {
        var source = db.ClientErrors.AsNoTracking();
        if (where is not null)
        {
            source = source.Where(where);
        }

        var rows = await source
            .GroupBy(e => new { e.Platform, e.ExceptionType })
            .Select(g => new
            {
                g.Key.Platform,
                g.Key.ExceptionType,
                Count = g.Count(),
                Gyms = g.Select(e => e.GymId).Distinct().Count(),
                First = g.Min(e => e.OccurredAt),
                Last = g.Max(e => e.OccurredAt),
            })
            .OrderByDescending(x => x.Last)
            .Take(limit)
            .ToListAsync(ct);

        return rows.Select(r => new LogGroupAggregate(
            ErrorGroupKinds.Client, $"{r.Platform}{Sep}{r.ExceptionType}", r.ExceptionType, r.Platform,
            r.Count, r.Gyms, r.First, r.Last, 0)).ToList();
    }

    private static string Short(string typeName)
    {
        var dot = typeName.LastIndexOf('.');
        return dot < 0 ? typeName : typeName[(dot + 1)..];
    }

    // ------------------------------------------------------------------ ocurrencias

    public async Task<IReadOnlyList<ErrorOccurrenceItem>> GetOccurrencesAsync(
        string kind, string fingerprint, int limit, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var parts = fingerprint.Split(Sep);

        switch (kind)
        {
            case ErrorGroupKinds.Error:
            {
                var rows = await db.Errors.AsNoTracking().Where(e => e.Fingerprint == fingerprint)
                    .OrderByDescending(e => e.OccurredAt).Take(limit).ToListAsync(cancellationToken);
                return rows.Select(e => new ErrorOccurrenceItem(
                    e.Id, e.OccurredAt, e.TraceId, TraceCode.Short(e.TraceId), e.GymId, e.UserId, e.Role, e.Method,
                    e.Path, e.Message, e.StackTrace, e.RequestSummary, null)).ToList();
            }

            case ErrorGroupKinds.EventFailure when parts.Length == 2:
            {
                var rows = await db.EventHandlerFailures.AsNoTracking()
                    .Where(e => e.EventType == parts[0] && e.HandlerType == parts[1])
                    .OrderByDescending(e => e.OccurredAt).Take(limit).ToListAsync(cancellationToken);
                return rows.Select(e => new ErrorOccurrenceItem(
                    e.Id, e.OccurredAt, e.TraceId, TraceCode.Short(e.TraceId), e.GymId, e.UserId, e.Role, null,
                    null, e.Message, e.StackTrace, null, e.RetryStatus)).ToList();
            }

            case ErrorGroupKinds.External when parts.Length == 3:
            {
                var status = int.TryParse(parts[2], out var code) ? (int?)code : null;
                var rows = await db.ExternalFailures.AsNoTracking()
                    .Where(e => e.Provider == parts[0] && e.Operation == parts[1] && e.StatusCode == status)
                    .OrderByDescending(e => e.OccurredAt).Take(limit).ToListAsync(cancellationToken);
                return rows.Select(e => new ErrorOccurrenceItem(
                    e.Id, e.OccurredAt, e.TraceId, TraceCode.Short(e.TraceId), e.GymId, e.UserId, e.Role, null,
                    null, e.ResponseBody ?? "(sin cuerpo)", null, $"intento {e.Attempt}, {e.DurationMs} ms", null)).ToList();
            }

            case ErrorGroupKinds.Client when parts.Length == 2:
            {
                var rows = await db.ClientErrors.AsNoTracking()
                    .Where(e => e.Platform == parts[0] && e.ExceptionType == parts[1])
                    .OrderByDescending(e => e.OccurredAt).Take(limit).ToListAsync(cancellationToken);
                return rows.Select(e => new ErrorOccurrenceItem(
                    e.Id, e.OccurredAt, e.TraceId, TraceCode.Short(e.TraceId), e.GymId, e.UserId, e.Role, null,
                    e.Route, e.Message, e.StackTrace, e.DeviceInfo, null)).ToList();
            }

            default:
                return [];
        }
    }

    // ------------------------------------------------------------------ estado de los grupos

    public async Task<IReadOnlyList<LogGroupState>> GetGroupStatesAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.ErrorGroups.AsNoTracking().ToListAsync(cancellationToken);
        return rows.Select(ToState).ToList();
    }

    public async Task<LogGroupState?> GetGroupStateAsync(string kind, string fingerprint, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.ErrorGroups.AsNoTracking()
            .SingleOrDefaultAsync(g => g.Kind == kind && g.Fingerprint == fingerprint, cancellationToken);
        return row is null ? null : ToState(row);
    }

    public async Task SaveGroupStateAsync(LogGroupState state, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.ErrorGroups.SingleOrDefaultAsync(
            g => g.Kind == state.Kind && g.Fingerprint == state.Fingerprint, cancellationToken);
        if (row is null)
        {
            row = new ErrorGroupRow { Kind = state.Kind, Fingerprint = state.Fingerprint };
            db.ErrorGroups.Add(row);
        }

        row.Title = state.Title;
        row.FirstSeenAt = state.FirstSeenAt;
        row.Status = state.Status;
        row.Notes = state.Notes;
        row.StatusChangedAt = state.StatusChangedAt;
        row.StatusChangedBy = state.StatusChangedBy;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static LogGroupState ToState(ErrorGroupRow r) => new(
        r.Kind, r.Fingerprint, r.Title, r.FirstSeenAt, r.Status, r.Notes, r.StatusChangedAt, r.StatusChangedBy);

    // ------------------------------------------------------------------ búsqueda por código

    public async Task<IReadOnlyList<TraceRecordItem>> SearchTraceAsync(
        string traceIdPrefix, int limit, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var p = traceIdPrefix;
        var result = new List<TraceRecordItem>();

        TraceRecordItem Make(string source, LogRowBase r, string summary, string? detail, int? status) => new(
            source, r.Id, r.OccurredAt, r.TraceId!, TraceCode.Short(r.TraceId)!, r.GymId, r.UserId, r.Role,
            summary, detail, status);

        foreach (var e in await db.Errors.AsNoTracking().Where(x => x.TraceId != null && x.TraceId.StartsWith(p))
                     .OrderByDescending(x => x.OccurredAt).Take(limit).ToListAsync(cancellationToken))
        {
            result.Add(Make("errors", e, $"{e.ExceptionType}: {e.Message}", $"{e.Method} {e.Endpoint ?? e.Path}".Trim(), 500));
        }

        foreach (var e in await db.EventHandlerFailures.AsNoTracking().Where(x => x.TraceId != null && x.TraceId.StartsWith(p))
                     .OrderByDescending(x => x.OccurredAt).Take(limit).ToListAsync(cancellationToken))
        {
            result.Add(Make("event_handler_failures", e, $"{Short(e.EventType)} → {Short(e.HandlerType)}", e.Message, null));
        }

        foreach (var e in await db.BusinessRejections.AsNoTracking().Where(x => x.TraceId != null && x.TraceId.StartsWith(p))
                     .OrderByDescending(x => x.OccurredAt).Take(limit).ToListAsync(cancellationToken))
        {
            result.Add(Make("business_rejections", e, e.Title ?? e.ExceptionType, e.Detail, e.StatusCode));
        }

        foreach (var e in await db.SecurityEvents.AsNoTracking().Where(x => x.TraceId != null && x.TraceId.StartsWith(p))
                     .OrderByDescending(x => x.OccurredAt).Take(limit).ToListAsync(cancellationToken))
        {
            result.Add(Make("security_events", e, e.EventType, e.Detail, null));
        }

        foreach (var e in await db.SlowRequests.AsNoTracking().Where(x => x.TraceId != null && x.TraceId.StartsWith(p))
                     .OrderByDescending(x => x.OccurredAt).Take(limit).ToListAsync(cancellationToken))
        {
            result.Add(Make("slow_requests", e, $"{e.Method} {e.Endpoint}".Trim(), $"{e.DurationMs} ms", e.StatusCode));
        }

        foreach (var e in await db.ExternalFailures.AsNoTracking().Where(x => x.TraceId != null && x.TraceId.StartsWith(p))
                     .OrderByDescending(x => x.OccurredAt).Take(limit).ToListAsync(cancellationToken))
        {
            result.Add(Make("external_failures", e, $"{e.Provider} · {e.Operation}", e.ResponseBody, e.StatusCode));
        }

        foreach (var e in await db.ClientErrors.AsNoTracking().Where(x => x.TraceId != null && x.TraceId.StartsWith(p))
                     .OrderByDescending(x => x.OccurredAt).Take(limit).ToListAsync(cancellationToken))
        {
            result.Add(Make("client_errors", e, $"{e.ExceptionType}: {e.Message}", e.Route, null));
        }

        return result;
    }

    // ------------------------------------------------------------------ eventos fallidos

    public async Task<(IReadOnlyList<EventFailureRecord> Items, int Total)> ListEventFailuresAsync(
        EventFailuresQuery query, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var source = db.EventHandlerFailures.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            source = source.Where(e => e.RetryStatus == query.Status);
        }

        if (!string.IsNullOrWhiteSpace(query.EventType))
        {
            source = source.Where(e => e.EventType == query.EventType);
        }

        if (!string.IsNullOrWhiteSpace(query.HandlerType))
        {
            source = source.Where(e => e.HandlerType == query.HandlerType);
        }

        var total = await source.CountAsync(cancellationToken);
        var rows = await source.OrderByDescending(e => e.OccurredAt)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(cancellationToken);
        return (rows.Select(ToRecord).ToList(), total);
    }

    public async Task<EventFailureRecord?> GetEventFailureAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.EventHandlerFailures.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    public async Task<IReadOnlyList<Guid>> GetRetryableIdsAsync(
        string eventType, string handlerType, int limit, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.EventHandlerFailures.AsNoTracking()
            .Where(e => e.EventType == eventType && e.HandlerType == handlerType
                && (e.RetryStatus == "pending" || e.RetryStatus == "retried_failed"))
            .OrderBy(e => e.OccurredAt).Select(e => e.Id).Take(limit).ToListAsync(cancellationToken);
    }

    public async Task<bool> MarkEventFailureAsync(
        Guid id, string retryStatus, DateTimeOffset at, Guid? by, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var updated = await db.EventHandlerFailures.Where(e => e.Id == id).ExecuteUpdateAsync(
            s => s.SetProperty(e => e.RetryStatus, retryStatus)
                .SetProperty(e => e.RetriedAt, at)
                .SetProperty(e => e.RetriedBy, by), cancellationToken);
        return updated > 0;
    }

    private static EventFailureRecord ToRecord(EventHandlerFailureRow r) => new(
        r.Id, r.OccurredAt, r.TraceId, r.GymId, r.EventType, r.HandlerType, r.Payload, r.ExceptionType, r.Message,
        r.RetryStatus, r.RetriedAt, r.RetriedBy);
}
