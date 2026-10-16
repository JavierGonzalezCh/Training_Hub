using GSM.Application.Common.Logs;
using GSM.Application.Logs;
using GSM.Application.Logs.Alerts;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Logs;

/// <summary>
/// <see cref="IAlertSignalSource"/>: las mediciones de las alertas sobre <see cref="LogsDbContext"/>. Todas filtran por
/// <c>occurred_at</c> primero, así que en las tablas particionadas solo tocan las particiones del mes en curso.
/// La huella de un evento fallido es la misma que usa el panel (<c>evento|manejador</c>) para que el grupo coincida.
/// </summary>
public sealed class AlertSignalSource(IDbContextFactory<LogsDbContext> contextFactory) : IAlertSignalSource
{
    private const char Sep = '|';

    public async Task<IReadOnlyList<RecentFailureGroup>> GetFailureGroupsSinceAsync(
        DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var result = new List<RecentFailureGroup>();

        var errors = await db.Errors.AsNoTracking()
            .Where(e => e.OccurredAt >= since)
            .GroupBy(e => e.Fingerprint)
            .Select(g => new
            {
                Fingerprint = g.Key,
                Type = g.Max(e => e.ExceptionType)!,
                Endpoint = g.Max(e => e.Endpoint),
                Count = g.Count(),
            })
            .ToListAsync(cancellationToken);
        result.AddRange(errors.Select(e => new RecentFailureGroup(
            ErrorGroupKinds.Error, e.Fingerprint, e.Type, e.Endpoint, e.Count)));

        var events = await db.EventHandlerFailures.AsNoTracking()
            .Where(e => e.OccurredAt >= since)
            .GroupBy(e => new { e.EventType, e.HandlerType })
            .Select(g => new { g.Key.EventType, g.Key.HandlerType, Count = g.Count() })
            .ToListAsync(cancellationToken);
        result.AddRange(events.Select(e => new RecentFailureGroup(
            ErrorGroupKinds.EventFailure, e.EventType + Sep + e.HandlerType,
            $"{Short(e.EventType)} → {Short(e.HandlerType)}", null, e.Count)));

        return result;
    }

    public async Task<IReadOnlyList<HeartbeatInfo>> GetHeartbeatsAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Heartbeats.AsNoTracking().ToListAsync(cancellationToken);
        return rows.Select(h => new HeartbeatInfo(h.Process, h.LastBeatAt)).ToList();
    }

    public async Task<IReadOnlyList<ProviderFailureCount>> GetExternalFailureCountsSinceAsync(
        DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.ExternalFailures.AsNoTracking()
            .Where(e => e.OccurredAt >= since)
            .GroupBy(e => e.Provider)
            .Select(g => new { Provider = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new ProviderFailureCount(r.Provider, r.Count)).ToList();
    }

    public async Task<IReadOnlyList<IpFailureCount>> GetLoginFailureCountsSinceAsync(
        DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.SecurityEvents.AsNoTracking()
            .Where(e => e.OccurredAt >= since && e.EventType == SecurityEventTypes.LoginFailed && e.Ip != null)
            .GroupBy(e => e.Ip!)
            .Select(g => new { Ip = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new IpFailureCount(r.Ip, r.Count)).ToList();
    }

    private static string Short(string typeName)
    {
        var dot = typeName.LastIndexOf('.');
        return dot < 0 ? typeName : typeName[(dot + 1)..];
    }
}
