using GSM.Application.Attendance.Abstractions;
using GSM.Domain.Attendance;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Attendance;

public sealed class CheckinAttemptRepository(GsmDbContext dbContext) : ICheckinAttemptRepository
{
    private DbSet<CheckinAttempt> Attempts => dbContext.Set<CheckinAttempt>();

    public void Add(CheckinAttempt attempt) => Attempts.Add(attempt);

    public Task<CheckinAttempt?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => Attempts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<CheckinAttempt?> GetPendingForMemberOnDateAsync(
        Guid memberId, DateOnly date, CheckinRejectionReason reason, CancellationToken cancellationToken = default)
        => Attempts.FirstOrDefaultAsync(
            a => a.MemberId == memberId && a.AttemptDate == date && a.Reason == reason && a.AcknowledgedAt == null,
            cancellationToken);

    public async Task<(IReadOnlyList<CheckinAttempt> Items, int Total)> GetPageAsync(
        AttemptPageQuery query, CancellationToken cancellationToken = default)
    {
        var q = Attempts.AsQueryable();

        if (query.From is { } from)
        {
            q = q.Where(a => a.AttemptDate >= from);
        }

        if (query.To is { } to)
        {
            q = q.Where(a => a.AttemptDate <= to);
        }

        if (query.PendingOnly)
        {
            q = q.Where(a => a.AcknowledgedAt == null);
        }

        if (query.Reason is { } reason)
        {
            q = q.Where(a => a.Reason == reason);
        }

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderByDescending(a => a.AttemptedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }
}
