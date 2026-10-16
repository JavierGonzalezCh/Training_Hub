using GSM.Application.Logs.Alerts;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Logs;

/// <summary>
/// <see cref="IPlatformAlertStore"/> sobre <see cref="LogsDbContext"/> (<c>logs.alert_state</c> y
/// <c>logs.platform_alerts</c>). Reclamar una clave y reservar un intento de entrega son <c>INSERT … ON CONFLICT</c> y
/// <c>UPDATE … WHERE</c> condicionales: la base decide quién gana, sin candados en memoria.
/// </summary>
public sealed class PlatformAlertStore(IDbContextFactory<LogsDbContext> contextFactory) : IPlatformAlertStore
{
    public async Task<Guid?> TryRaiseAsync(
        AlertCandidate candidate, DateTimeOffset now, TimeSpan? repeatAfter, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        int claimed;
        if (repeatAfter is null)
        {
            claimed = await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT INTO logs.alert_state (key, last_alerted_at) VALUES ({candidate.Key}, {now})
                 ON CONFLICT (key) DO NOTHING
                 """, cancellationToken);
        }
        else
        {
            var cutoff = now - repeatAfter.Value;
            claimed = await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT INTO logs.alert_state (key, last_alerted_at) VALUES ({candidate.Key}, {now})
                 ON CONFLICT (key) DO UPDATE SET last_alerted_at = EXCLUDED.last_alerted_at
                 WHERE logs.alert_state.last_alerted_at <= {cutoff}
                 """, cancellationToken);
        }

        if (claimed == 0)
        {
            return null;
        }

        var row = new PlatformAlertRow
        {
            Id = Guid.CreateVersion7(),
            CreatedAt = now,
            Key = candidate.Key,
            Kind = candidate.Kind,
            Title = candidate.Title,
            Body = candidate.Body,
            Link = candidate.Link,
        };
        db.PlatformAlerts.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return row.Id;
    }

    public async Task<IReadOnlyList<PlatformAlertRecord>> ListDueForDeliveryAsync(
        DateTimeOffset now, int take, int maxAttempts, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.PlatformAlerts.AsNoTracking()
            .Where(a => (a.EmailStatus == PlatformAlertStatuses.Pending || a.PushStatus == PlatformAlertStatuses.Pending)
                        && a.Attempts < maxAttempts
                        && (a.NextAttemptAt == null || a.NextAttemptAt <= now))
            .OrderBy(a => a.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);
        return rows.Select(ToRecord).ToList();
    }

    public async Task<bool> TryBeginDeliveryAsync(
        Guid id, DateTimeOffset now, TimeSpan holdFor, int maxAttempts, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var next = now + holdFor;
        var affected = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE logs.platform_alerts
             SET attempts = attempts + 1, next_attempt_at = {next}
             WHERE id = {id}
               AND attempts < {maxAttempts}
               AND (email_status = 'pending' OR push_status = 'pending')
               AND (next_attempt_at IS NULL OR next_attempt_at <= {now})
             """, cancellationToken);
        return affected == 1;
    }

    public async Task CompleteDeliveryAsync(
        Guid id, string emailStatus, string pushStatus, string? error, int maxAttempts, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.PlatformAlerts.SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (row is null)
        {
            return;
        }

        var exhausted = row.Attempts >= maxAttempts;
        row.EmailStatus = Settle(emailStatus, exhausted);
        row.PushStatus = Settle(pushStatus, exhausted);
        row.LastError = error is null ? null : error.Length > 500 ? error[..500] : error;
        if (row.EmailStatus != PlatformAlertStatuses.Pending && row.PushStatus != PlatformAlertStatuses.Pending)
        {
            row.NextAttemptAt = null;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<PlatformAlertRecord> Items, int Total)> ListAsync(
        int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var total = await db.PlatformAlerts.CountAsync(cancellationToken);
        var rows = await db.PlatformAlerts.AsNoTracking()
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (rows.Select(ToRecord).ToList(), total);
    }

    /// <summary>Un canal que falló sigue pendiente (se reintenta) mientras queden intentos; al agotarse queda fallido.</summary>
    private static string Settle(string status, bool exhausted)
        => status == PlatformAlertStatuses.Failed && !exhausted ? PlatformAlertStatuses.Pending : status;

    private static PlatformAlertRecord ToRecord(PlatformAlertRow a) => new(
        a.Id, a.CreatedAt, a.Key, a.Kind, a.Title, a.Body, a.Link, a.EmailStatus, a.PushStatus, a.Attempts, a.LastError);
}
