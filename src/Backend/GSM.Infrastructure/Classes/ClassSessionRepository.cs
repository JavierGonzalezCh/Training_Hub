using GSM.Application.Classes.Abstractions;
using GSM.Domain.Classes;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Classes;

/// <summary><see cref="ClassSession"/> es <c>ITenantEntity</c>: el Global Query Filter ya restringe al gym actual.</summary>
public sealed class ClassSessionRepository(GsmDbContext dbContext) : IClassSessionRepository
{
    private DbSet<ClassSession> Sessions => dbContext.Set<ClassSession>();

    public Task<ClassSession?> GetAsync(
        Guid classId, DateOnly sessionDate, CancellationToken cancellationToken = default)
        => Sessions.FirstOrDefaultAsync(
            s => s.ClassId == classId && s.SessionDate == sessionDate, cancellationToken);

    public async Task<IReadOnlyList<ClassSession>> ListInRangeAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        => await Sessions
            .Where(s => s.SessionDate >= from && s.SessionDate <= to)
            .ToListAsync(cancellationToken);

    public void Add(ClassSession session) => Sessions.Add(session);
}
