using GSM.Application.Attendance;
using GSM.Application.Attendance.Public;
using GSM.Application.Common.Abstractions;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using AttendanceEntity = GSM.Domain.Attendance.Attendance;

namespace GSM.Infrastructure.Attendance;

public sealed class AttendanceReader(GsmDbContext dbContext, IClock clock) : IAttendanceReader
{
    private IQueryable<AttendanceEntity> Attendances => dbContext.Set<AttendanceEntity>();

    public Task<int> GetVisitCountAsync(Guid memberId, CancellationToken cancellationToken = default)
        => Attendances.CountAsync(a => a.MemberId == memberId, cancellationToken);

    public async Task<int> GetStreakAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        var dates = await Attendances
            .Where(a => a.MemberId == memberId)
            .Select(a => a.AttendanceDate)
            .Distinct()
            .ToListAsync(cancellationToken);

        return StreakCalculator.Compute(dates, clock.Today);
    }

    public Task<int> GetTodayCountAsync(Guid gymId, CancellationToken cancellationToken = default)
        => Attendances.CountAsync(
            a => a.GymId == gymId && a.AttendanceDate == clock.Today, cancellationToken);

    public async Task<DateOnly?> GetLastVisitAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        var dates = await Attendances
            .Where(a => a.MemberId == memberId)
            .OrderByDescending(a => a.AttendanceDate)
            .Select(a => (DateOnly?)a.AttendanceDate)
            .FirstOrDefaultAsync(cancellationToken);

        return dates;
    }
}
