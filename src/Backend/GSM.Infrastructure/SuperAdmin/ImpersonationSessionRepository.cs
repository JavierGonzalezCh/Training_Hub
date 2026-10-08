using GSM.Application.Common.Abstractions;
using GSM.Application.SuperAdmin.Abstractions;
using GSM.Domain.Gyms;
using GSM.Domain.SuperAdmin;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GSM.Infrastructure.SuperAdmin;

public sealed class ImpersonationSessionRepository(GsmDbContext dbContext) : IImpersonationSessionRepository
{
    private DbSet<ImpersonationSession> Sessions => dbContext.Set<ImpersonationSession>();

    public void Add(ImpersonationSession session) => Sessions.Add(session);

    public Task<ImpersonationSession?> GetByIdAsync(Guid sessionId, CancellationToken cancellationToken = default)
        => Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

    public Task<ImpersonationSession?> GetActiveForUserAsync(
        Guid superAdminUserId, CancellationToken cancellationToken = default)
        => Sessions
            .Where(s => s.SuperAdminUserId == superAdminUserId && s.EndedAt == null)
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<ImpersonationAuditRow>> GetHistoryAsync(
        Guid? gymId, int limit, CancellationToken cancellationToken = default)
        => await (
                from s in Sessions.AsNoTracking()
                join g in dbContext.Set<Gym>().IgnoreQueryFilters() on s.GymId equals g.Id
                where gymId == null || s.GymId == gymId
                orderby s.StartedAt descending
                select new ImpersonationAuditRow(
                    s.Id, s.SuperAdminUserId, s.GymId, g.Name, s.Reason, s.Mode,
                    dbContext.Set<ImpersonationAction>().Count(a => a.SessionId == s.Id),
                    s.StartedAt, s.EndedAt))
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ImpersonationAction>> GetActionsAsync(
        Guid sessionId, int limit, CancellationToken cancellationToken = default)
        => await dbContext.Set<ImpersonationAction>().AsNoTracking()
            .Where(a => a.SessionId == sessionId)
            .OrderBy(a => a.OccurredAt)
            .Take(Math.Clamp(limit, 1, 1000))
            .ToListAsync(cancellationToken);
}

/// <summary>
/// Guarda cada acción impersonada con su propio ámbito de DI (su propio <see cref="GsmDbContext"/>), así
/// que queda fuera de la transacción del caso de uso: un fallo de auditoría no revierte ni frena la
/// operación. Si guardar falla, se loguea y se sigue.
/// </summary>
public sealed class ImpersonationActionRecorder(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILogger<ImpersonationActionRecorder> logger) : IImpersonationActionRecorder
{
    public async Task RecordAsync(
        Guid sessionId,
        string method,
        string endpoint,
        string summary,
        int statusCode,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var action = ImpersonationAction.Record(sessionId, method, endpoint, summary, statusCode, clock.UtcNow);
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            db.Set<ImpersonationAction>().Add(action);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex, "No se pudo auditar la acción {Method} {Endpoint} de la impersonación {SessionId}",
                method, endpoint, sessionId);
        }
    }
}
