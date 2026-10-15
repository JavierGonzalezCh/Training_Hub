using GSM.Application.Classes.Abstractions;
using GSM.Domain.Classes;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Classes;

/// <summary><see cref="WaitlistEntry"/> es <c>ITenantEntity</c>: el Global Query Filter restringe al gym actual (la tarea periódica no tiene gym y ve todos).</summary>
public sealed class WaitlistRepository(GsmDbContext dbContext) : IWaitlistRepository
{
    private DbSet<WaitlistEntry> Entries => dbContext.Set<WaitlistEntry>();

    private IQueryable<WaitlistEntry> Open
        => Entries.Where(w => w.Status == WaitlistEntryStatus.Waiting || w.Status == WaitlistEntryStatus.Offered);

    public Task<WaitlistEntry?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => Entries.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

    public Task<WaitlistEntry?> GetOpenAsync(Guid sessionId, Guid memberId, CancellationToken cancellationToken = default)
        => Open.FirstOrDefaultAsync(w => w.SessionId == sessionId && w.MemberId == memberId, cancellationToken);

    public async Task<IReadOnlyList<WaitlistEntry>> ListOpenForSessionAsync(
        Guid sessionId, CancellationToken cancellationToken = default)
        => await Open.Where(w => w.SessionId == sessionId).OrderBy(w => w.Position).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WaitlistEntry>> ListOpenForSessionsAsync(
        IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken = default)
        => sessionIds.Count == 0
            ? []
            : await Open.Where(w => sessionIds.Contains(w.SessionId)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<(WaitlistEntry Entry, ClassSession Session, GymClass Class)>> ListOpenForMemberAsync(
        Guid memberId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        var query =
            from w in Open.Where(w => w.MemberId == memberId)
            join s in dbContext.Set<ClassSession>() on w.SessionId equals s.Id
            join c in dbContext.Set<GymClass>() on s.ClassId equals c.Id
            select new { w, s, c };

        if (from is { } f)
        {
            query = query.Where(x => x.s.SessionDate >= f);
        }

        if (to is { } t)
        {
            query = query.Where(x => x.s.SessionDate <= t);
        }

        var rows = await query.ToListAsync(cancellationToken);
        return rows.Select(x => (x.w, x.s, x.c)).ToList();
    }

    public async Task<int> GetMaxPositionAsync(Guid sessionId, CancellationToken cancellationToken = default)
        => await Entries.Where(w => w.SessionId == sessionId).MaxAsync(w => (int?)w.Position, cancellationToken) ?? 0;

    public Task<int> CountOffersAsync(Guid sessionId, CancellationToken cancellationToken = default)
        => Entries.CountAsync(
            w => w.SessionId == sessionId && w.Status == WaitlistEntryStatus.Offered, cancellationToken);

    public async Task<int> CountFutureOpenForMemberAsync(
        Guid memberId, DateOnly today, TimeOnly nowTime, CancellationToken cancellationToken = default)
        => await (
            from w in Open.Where(w => w.MemberId == memberId)
            join s in dbContext.Set<ClassSession>() on w.SessionId equals s.Id
            where s.SessionDate > today || (s.SessionDate == today && s.StartTime > nowTime)
            select w.Id).CountAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListSessionIdsWithOpenEntriesAsync(CancellationToken cancellationToken = default)
        => await Open.Select(w => w.SessionId).Distinct().ToListAsync(cancellationToken);

    public void Add(WaitlistEntry entry) => Entries.Add(entry);
}
