using GSM.Application.SuperAdmin.Abstractions;
using GSM.Domain.Gyms;
using GSM.Domain.SuperAdmin;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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
                    s.Id, s.SuperAdminUserId, s.GymId, g.Name, s.Reason, s.StartedAt, s.EndedAt))
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken);
}
