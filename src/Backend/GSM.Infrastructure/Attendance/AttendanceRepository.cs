using GSM.Application.Attendance.Abstractions;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using AttendanceEntity = GSM.Domain.Attendance.Attendance;

namespace GSM.Infrastructure.Attendance;

public sealed class AttendanceRepository(GsmDbContext dbContext) : IAttendanceRepository
{
    private DbSet<AttendanceEntity> Attendances => dbContext.Set<AttendanceEntity>();

    public void Add(AttendanceEntity attendance) => Attendances.Add(attendance);

    public Task<AttendanceEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => Attendances.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<AttendanceEntity?> GetForMemberOnDateAsync(
        Guid memberId, DateOnly date, CancellationToken cancellationToken = default)
        => Attendances.FirstOrDefaultAsync(
            a => a.MemberId == memberId && a.AttendanceDate == date && a.VoidedAt == null, cancellationToken);
    // Con entradas múltiples puede haber varias; esto solo responde "¿entró ya hoy?".

    public async Task<IReadOnlyList<AttendanceEntity>> GetEntriesOnDateAsync(
        Guid memberId, DateOnly date, CancellationToken cancellationToken = default)
        => await Attendances
            .Where(a => a.MemberId == memberId && a.AttendanceDate == date && a.VoidedAt == null)
            .OrderBy(a => a.CheckedInAt)
            .ToListAsync(cancellationToken);

    public async Task<(IReadOnlyList<AttendanceEntity> Items, int Total)> GetPageAsync(
        AttendancePageQuery query, CancellationToken cancellationToken = default)
    {
        var q = Attendances.AsQueryable();

        if (!query.IncludeVoided)
        {
            q = q.Where(a => a.VoidedAt == null);
        }

        if (query.MemberId is { } memberId)
        {
            q = q.Where(a => a.MemberId == memberId && a.VoidedAt == null);
        }

        if (query.From is { } from)
        {
            q = q.Where(a => a.AttendanceDate >= from);
        }

        if (query.To is { } to)
        {
            q = q.Where(a => a.AttendanceDate <= to);
        }

        if (query.AuthorizedOnly)
        {
            q = q.Where(a => a.AuthorizedBy != null);
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
            .Where(a => a.MemberId == memberId && a.VoidedAt == null)
            .Select(a => a.AttendanceDate)
            .Distinct()
            .ToListAsync(cancellationToken);

    public Task<int> GetVisitCountAsync(Guid memberId, CancellationToken cancellationToken = default)
        => Attendances.Where(a => a.MemberId == memberId && a.VoidedAt == null).Select(a => a.AttendanceDate).Distinct().CountAsync(cancellationToken);

    public Task<int> GetTodayCountAsync(
        Guid gymId, DateOnly today, CancellationToken cancellationToken = default)
        => Attendances.CountAsync(a => a.GymId == gymId && a.AttendanceDate == today && a.VoidedAt == null, cancellationToken);

    public async Task<IReadOnlyList<DateTimeOffset>> GetCheckInInstantsAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
        => await Attendances
            .Where(a => a.CheckedInAt >= fromUtc && a.CheckedInAt < toUtc && a.VoidedAt == null)
            .Select(a => a.CheckedInAt)
            .ToListAsync(cancellationToken);
}
