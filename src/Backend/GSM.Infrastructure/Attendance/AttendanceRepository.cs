using GSM.Application.Attendance.Abstractions;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using AttendanceEntity = GSM.Domain.Attendance.Attendance;

namespace GSM.Infrastructure.Attendance;

public sealed class AttendanceRepository(GsmDbContext dbContext) : IAttendanceRepository
{
    private DbSet<AttendanceEntity> Attendances => dbContext.Set<AttendanceEntity>();

    public void Add(AttendanceEntity attendance) => Attendances.Add(attendance);

    public Task<AttendanceEntity?> GetForMemberOnDateAsync(
        Guid memberId, DateOnly date, CancellationToken cancellationToken = default)
        => Attendances.FirstOrDefaultAsync(
            a => a.MemberId == memberId && a.AttendanceDate == date, cancellationToken);

    public async Task<(IReadOnlyList<AttendanceEntity> Items, int Total)> GetPageAsync(
        AttendancePageQuery query, CancellationToken cancellationToken = default)
    {
        var q = Attendances.AsQueryable();

        if (query.MemberId is { } memberId)
        {
            q = q.Where(a => a.MemberId == memberId);
        }

        if (query.From is { } from)
        {
            q = q.Where(a => a.AttendanceDate >= from);
        }

        if (query.To is { } to)
        {
            q = q.Where(a => a.AttendanceDate <= to);
        }

        var total = await q.CountAsync(cancellationToken);

        var items = await q
            .OrderByDescending(a => a.CheckedInAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<IReadOnlyList<DateOnly>> GetMemberDatesAsync(
        Guid memberId, CancellationToken cancellationToken = default)
        => await Attendances
            .Where(a => a.MemberId == memberId)
            .Select(a => a.AttendanceDate)
            .Distinct()
            .ToListAsync(cancellationToken);

    public Task<int> GetVisitCountAsync(Guid memberId, CancellationToken cancellationToken = default)
        => Attendances.CountAsync(a => a.MemberId == memberId, cancellationToken);

    public Task<int> GetTodayCountAsync(
        Guid gymId, DateOnly today, CancellationToken cancellationToken = default)
        => Attendances.CountAsync(a => a.GymId == gymId && a.AttendanceDate == today, cancellationToken);

    public async Task<IReadOnlyList<DateTimeOffset>> GetCheckInInstantsAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
        => await Attendances
            .Where(a => a.CheckedInAt >= fromUtc && a.CheckedInAt < toUtc)
            .Select(a => a.CheckedInAt)
            .ToListAsync(cancellationToken);
}
