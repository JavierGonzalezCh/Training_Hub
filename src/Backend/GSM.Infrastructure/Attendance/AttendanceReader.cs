using GSM.Application.Attendance;
using GSM.Application.Attendance.Public;
using GSM.Application.Common.Abstractions;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using AttendanceEntity = GSM.Domain.Attendance.Attendance;

namespace GSM.Infrastructure.Attendance;

public sealed class AttendanceReader(GsmDbContext dbContext, IClock clock) : IAttendanceReader
{
    // Las entradas anuladas no cuentan para nada (visitas, racha, mes, último día).
    private IQueryable<AttendanceEntity> Attendances => dbContext.Set<AttendanceEntity>().Where(a => a.VoidedAt == null);

    // Visitas = días con entrada, no entradas: los logros cuentan días (A1b, §3.2).
    public Task<int> GetVisitCountAsync(Guid memberId, CancellationToken cancellationToken = default)
        => Attendances.Where(a => a.MemberId == memberId).Select(a => a.AttendanceDate).Distinct().CountAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> GetVisitCountsAsync(
        IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken = default)
    {
        if (memberIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        return await Attendances
            .Where(a => memberIds.Contains(a.MemberId))
            .GroupBy(a => a.MemberId)
            .Select(g => new { MemberId = g.Key, Days = g.Select(a => a.AttendanceDate).Distinct().Count() })
            .ToDictionaryAsync(x => x.MemberId, x => x.Days, cancellationToken);
    }

    public async Task<int> GetStreakAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        var dates = await Attendances
            .Where(a => a.MemberId == memberId)
            .Select(a => a.AttendanceDate)
            .Distinct()
            .ToListAsync(cancellationToken);

        return StreakCalculator.Compute(dates, clock.Today);
    }

    public async Task<IReadOnlyList<DateOnly>> GetAttendanceDatesAsync(
        Guid memberId, DateOnly? from = null, CancellationToken cancellationToken = default)
        => await Attendances
            .Where(a => a.MemberId == memberId && (from == null || a.AttendanceDate >= from))
            .Select(a => a.AttendanceDate)
            .Distinct()
            .ToListAsync(cancellationToken);

    public Task<int> GetTodayCountAsync(Guid gymId, CancellationToken cancellationToken = default)
        => Attendances.CountAsync(
            a => a.GymId == gymId && a.AttendanceDate == clock.Today, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> GetMonthlyCountsByGymAsync(
        int year, int month, CancellationToken cancellationToken = default)
    {
        // AttendanceDate ya es un día de negocio (DateOnly): el mes se acota por fecha, sin rango UTC.
        var firstDay = new DateOnly(year, month, 1);
        var firstOfNext = firstDay.AddMonths(1);

        return await Attendances
            .Where(a => a.AttendanceDate >= firstDay && a.AttendanceDate < firstOfNext)
            .GroupBy(a => a.GymId)
            .Select(g => new { GymId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.GymId, x => x.Count, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, DateOnly>> GetLastAttendanceDateByGymAsync(
        CancellationToken cancellationToken = default)
        => await Attendances
            .GroupBy(a => a.GymId)
            .Select(g => new { GymId = g.Key, Last = g.Max(a => a.AttendanceDate) })
            .ToDictionaryAsync(x => x.GymId, x => x.Last, cancellationToken);

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
