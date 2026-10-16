using System.Globalization;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Logs;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Logs;

/// <summary>
/// Purga diaria del schema <c>logs</c> (docs/GSM-logs.md §9). Corre dentro de la tarea periódica, como máximo
/// una vez al día (lo recuerda el latido <see cref="PurgeHeartbeat"/>). <c>errors</c> va particionada por mes:
/// borrar un mes viejo es soltar su partición. Un mes solo se suelta cuando <b>termina</b> antes del corte de
/// retención, así que los meses parcialmente viejos se recortan con un DELETE normal (una sola partición).
/// </summary>
public sealed class LogMaintenance(IDbContextFactory<LogsDbContext> contextFactory, IClock clock) : ILogMaintenance
{
    public const string PurgeHeartbeat = "logs_purge";

    /// <summary>Tablas particionadas por mes sobre <c>occurred_at</c>; sus particiones se llaman <c>&lt;tabla&gt;_YYYYMM</c>.</summary>
    public static readonly string[] PartitionedTables =
        ["errors", "business_rejections", "security_events", "slow_requests", "external_failures", "client_errors"];

    /// <summary>Cuántos meses por delante mantiene creados la purga.</summary>
    public const int MonthsAhead = 12;

    public async Task<LogPurgeResult?> RunDailyIfDueAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var lastRun = await db.Heartbeats
            .Where(h => h.Process == PurgeHeartbeat)
            .Select(h => (DateTimeOffset?)h.LastBeatAt)
            .SingleOrDefaultAsync(cancellationToken);
        if (lastRun is { } last && last >= clock.BusinessDayRange(clock.Today).Start)
        {
            return null;
        }

        var now = clock.UtcNow;
        var cutoff = now.AddDays(-ILogMaintenance.RetentionDays);

        var created = 0;
        var dropped = 0;
        var rows = 0;
        foreach (var table in PartitionedTables)
        {
            created += await EnsurePartitionsAsync(db, table, now, cancellationToken);
            dropped += await DropOldPartitionsAsync(db, table, cutoff, cancellationToken);

            // El mes que cruza el corte no se suelta entero: se recorta con un DELETE (una sola partición).
            // El nombre sale de la lista fija de arriba, no de un usuario.
#pragma warning disable EF1002, EF1003
            rows += await db.Database.ExecuteSqlRawAsync(
                $"DELETE FROM logs.\"{table}\" WHERE occurred_at < {{0}}", new object[] { cutoff }, cancellationToken);
#pragma warning restore EF1002, EF1003
        }

        rows += await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM logs.event_handler_failures WHERE occurred_at < {cutoff}", cancellationToken);
        rows += await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM logs.job_runs WHERE occurred_at < {cutoff}", cancellationToken);

        // LG5: las alertas al SuperAdmin y sus claves de repetición también duran un año.
        rows += await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM logs.platform_alerts WHERE created_at < {cutoff}", cancellationToken);
        rows += await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM logs.alert_state WHERE last_alerted_at < {cutoff}", cancellationToken);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO logs.heartbeats (process, last_beat_at) VALUES ({PurgeHeartbeat}, {clock.UtcNow})
             ON CONFLICT (process) DO UPDATE SET last_beat_at = EXCLUDED.last_beat_at
             """, cancellationToken);

        return new LogPurgeResult(created, dropped, rows);
    }

    /// <summary>Crea (si falta) la partición mensual de <c>errors</c> que cubre <paramref name="instant"/>.</summary>
    public Task EnsureErrorPartitionAsync(DateTimeOffset instant, CancellationToken cancellationToken = default)
        => EnsurePartitionAsync("errors", instant, cancellationToken);

    /// <summary>Crea (si falta) la partición mensual de una tabla particionada de <see cref="PartitionedTables"/>.</summary>
    public async Task EnsurePartitionAsync(string table, DateTimeOffset instant, CancellationToken cancellationToken = default)
    {
        if (!PartitionedTables.Contains(table))
        {
            throw new ArgumentException($"'{table}' no es una tabla particionada de logs.", nameof(table));
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await CreatePartitionAsync(db, table, MonthStart(instant), cancellationToken);
    }

    private static async Task<int> EnsurePartitionsAsync(
        LogsDbContext db, string table, DateTimeOffset now, CancellationToken ct)
    {
        var existing = (await ExistingPartitionsAsync(db, table, ct)).Select(p => p.Name).ToHashSet();
        var first = MonthStart(now);
        var created = 0;
        for (var i = 0; i <= MonthsAhead; i++)
        {
            var month = first.AddMonths(i);
            if (!existing.Contains(PartitionName(table, month)))
            {
                await CreatePartitionAsync(db, table, month, ct);
                created++;
            }
        }

        return created;
    }

    private static async Task<int> DropOldPartitionsAsync(
        LogsDbContext db, string table, DateTimeOffset cutoff, CancellationToken ct)
    {
        var dropped = 0;
        foreach (var (name, monthStart) in await ExistingPartitionsAsync(db, table, ct))
        {
            if (monthStart.AddMonths(1) <= cutoff)
            {
                // El nombre sale de nuestro propio patrón <tabla>_YYYYMM (validado al listar), no de un usuario.
#pragma warning disable EF1002, EF1003
                await db.Database.ExecuteSqlRawAsync($"DROP TABLE IF EXISTS logs.\"{name}\"", ct);
#pragma warning restore EF1002, EF1003
                dropped++;
            }
        }

        return dropped;
    }

    private static async Task<List<(string Name, DateTimeOffset MonthStart)>> ExistingPartitionsAsync(
        LogsDbContext db, string table, CancellationToken ct)
    {
        var parent = "logs." + table;
        var names = await db.Database.SqlQuery<string>(
            $"""
            SELECT c.relname AS "Value"
            FROM pg_inherits i
            JOIN pg_class c ON c.oid = i.inhrelid
            WHERE i.inhparent = {parent}::regclass
            """).ToListAsync(ct);
        var prefix = table + "_";

        var partitions = new List<(string, DateTimeOffset)>();
        foreach (var name in names)
        {
            if (name.Length == prefix.Length + "YYYYMM".Length
                && name.StartsWith(prefix, StringComparison.Ordinal)
                && DateTime.TryParseExact(name[prefix.Length..], "yyyyMM", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var month))
            {
                partitions.Add((name, new DateTimeOffset(month, TimeSpan.Zero)));
            }
        }

        return partitions;
    }

    private static async Task CreatePartitionAsync(
        LogsDbContext db, string table, DateTimeOffset monthStart, CancellationToken ct)
    {
        var from = monthStart.UtcDateTime.ToString("yyyy-MM-dd'T'00:00:00'+00'", CultureInfo.InvariantCulture);
        var to = monthStart.AddMonths(1).UtcDateTime.ToString("yyyy-MM-dd'T'00:00:00'+00'", CultureInfo.InvariantCulture);
#pragma warning disable EF1002, EF1003
        await db.Database.ExecuteSqlRawAsync(
            $"CREATE TABLE IF NOT EXISTS logs.\"{PartitionName(table, monthStart)}\" PARTITION OF logs.\"{table}\" " +
            $"FOR VALUES FROM ('{from}') TO ('{to}')", ct);
#pragma warning restore EF1002, EF1003
    }

    private static string PartitionName(string table, DateTimeOffset monthStart) =>
        $"{table}_{monthStart.UtcDateTime.ToString("yyyyMM", CultureInfo.InvariantCulture)}";

    private static DateTimeOffset MonthStart(DateTimeOffset instant) =>
        new(instant.UtcDateTime.Year, instant.UtcDateTime.Month, 1, 0, 0, 0, TimeSpan.Zero);
}
