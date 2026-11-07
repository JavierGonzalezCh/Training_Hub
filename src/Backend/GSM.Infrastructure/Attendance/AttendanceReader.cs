using GSM.Application.Attendance;
using GSM.Application.Attendance.Abstractions;
using GSM.Application.Gyms.Public;
using GSM.Domain.Members;
using GSM.Application.Attendance.Public;
using GSM.Application.Common.Abstractions;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using AttendanceEntity = GSM.Domain.Attendance.Attendance;

namespace GSM.Infrastructure.Attendance;

public sealed class AttendanceReader(
    GsmDbContext dbContext,
    IClock clock,
    IGymReader gyms,
    ICheckinSettingsRepository checkinSettings,
    IAttendanceRepository attendanceRepository) : IAttendanceReader
{
    // Las entradas anuladas no cuentan para nada (visitas, racha, mes, último día).
    private IQueryable<AttendanceEntity> Attendances => dbContext.Set<AttendanceEntity>().Where(a => a.VoidedAt == null);

    // A6: lo que cuenta para logros y racha excluye las entradas con QR fijo fuera del radio o sin ubicación.
    private IQueryable<AttendanceEntity> ProgressAttendances => Attendances
        .Where(a => a.LocationCheck == null || a.LocationCheck == GSM.Domain.Attendance.LocationCheck.Inside);

    // Visitas = días con entrada, no entradas: los logros cuentan días (A1b, §3.2).
    public Task<int> GetVisitCountAsync(Guid memberId, CancellationToken cancellationToken = default)
        => ProgressAttendances.Where(a => a.MemberId == memberId).Select(a => a.AttendanceDate).Distinct().CountAsync(cancellationToken);

    public async Task<VisitHabits> GetVisitHabitsAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        var rows = await ProgressAttendances
            .Where(a => a.MemberId == memberId)
            .Select(a => new { a.AttendanceDate, a.CheckedInAt })
            .ToListAsync(cancellationToken);

        var local = rows.Select(r => (r.AttendanceDate, Hour: clock.ToBusinessLocal(r.CheckedInAt).Hour)).ToList();
        var early = local.Where(r => r.Hour < VisitHabits.EarlyBeforeHour).Select(r => r.AttendanceDate).Distinct().Count();
        var late = local.Where(r => r.Hour >= VisitHabits.LateFromHour).Select(r => r.AttendanceDate).Distinct().Count();
        var weekends = local
            .Select(r => r.AttendanceDate)
            .Where(d => d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            .Select(d => d.AddDays(d.DayOfWeek == DayOfWeek.Sunday ? -1 : 0))
            .Distinct()
            .Count();
        return new VisitHabits(early, late, weekends);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetVisitCountsAsync(
        IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken = default)
    {
        if (memberIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        return await ProgressAttendances
            .Where(a => memberIds.Contains(a.MemberId))
            .GroupBy(a => a.MemberId)
            .Select(g => new { MemberId = g.Key, Days = g.Select(a => a.AttendanceDate).Distinct().Count() })
            .ToDictionaryAsync(x => x.MemberId, x => x.Days, cancellationToken);
    }

    public async Task<IReadOnlyList<MemberDaysInRange>> GetDayCountsInRangeAsync(
        IReadOnlyCollection<Guid> memberIds, DateOnly from, DateOnly toExclusive,
        CancellationToken cancellationToken = default)
    {
        if (memberIds.Count == 0)
        {
            return [];
        }

        // AttendanceDate ya es un día de negocio (DateOnly): el rango se acota por fecha, sin rango UTC.
        var rows = await Attendances
            .Where(a => memberIds.Contains(a.MemberId) && a.AttendanceDate >= from && a.AttendanceDate < toExclusive)
            .Select(a => new { a.MemberId, a.AttendanceDate })
            .Distinct()
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.MemberId)
            .Select(g => new MemberDaysInRange(g.Key, g.Count(), g.Max(r => r.AttendanceDate)))
            .ToList();
    }

    public async Task<int> GetStreakAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        var dates = await ProgressAttendances
            .Where(a => a.MemberId == memberId)
            .Select(a => a.AttendanceDate)
            .Distinct()
            .ToListAsync(cancellationToken);

        return StreakCalculator.Compute(dates, clock.Today);
    }

    public async Task<IReadOnlyList<DateOnly>> GetAttendanceDatesAsync(
        Guid memberId, DateOnly? from = null, CancellationToken cancellationToken = default)
        => await ProgressAttendances
            .Where(a => a.MemberId == memberId && (from == null || a.AttendanceDate >= from))
            .Select(a => a.AttendanceDate)
            .Distinct()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<UncountedEntryDay>> GetUncountedEntryDaysAsync(
        Guid memberId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var rows = await Attendances
            .Where(a => a.MemberId == memberId && a.AttendanceDate >= from && a.AttendanceDate <= to)
            .Select(a => new { a.AttendanceDate, a.LocationCheck })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.AttendanceDate)
            // Un día con alguna entrada que sí cuenta no necesita explicación.
            .Where(g => g.All(r => r.LocationCheck is GSM.Domain.Attendance.LocationCheck.Outside or GSM.Domain.Attendance.LocationCheck.Denied))
            .Select(g => new UncountedEntryDay(
                g.Key, g.Any(r => r.LocationCheck == GSM.Domain.Attendance.LocationCheck.Outside)))
            .OrderBy(d => d.Date)
            .ToList();
    }

    public Task<int> GetTodayCountAsync(Guid gymId, CancellationToken cancellationToken = default)
        => Attendances
            .Where(a => a.GymId == gymId && a.AttendanceDate == clock.Today && a.VoidedAt == null)
            .Select(a => a.MemberId)
            .Distinct()
            .CountAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> GetMonthlyCountsByGymAsync(
        int year, int month, CancellationToken cancellationToken = default)
    {
        // AttendanceDate ya es un día de negocio (DateOnly): el mes se acota por fecha, sin rango UTC.
        var firstDay = new DateOnly(year, month, 1);
        var firstOfNext = firstDay.AddMonths(1);

        return await Attendances
            .Where(a => a.AttendanceDate >= firstDay && a.AttendanceDate < firstOfNext && a.VoidedAt == null)
            .GroupBy(a => a.GymId)
            .Select(g => new { GymId = g.Key, Count = g.Select(a => a.MemberId).Distinct().Count() })
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

    public async Task<DateTimeOffset?> GetFirstEntryTodayAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        var today = clock.Today;
        return await Attendances
            .Where(a => a.MemberId == memberId && a.AttendanceDate == today)
            .OrderBy(a => a.CheckedInAt)
            .Select(a => (DateTimeOffset?)a.CheckedInAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<bool> HasCheckinSettingsAsync(Guid gymId, CancellationToken cancellationToken = default)
        => dbContext.Set<GSM.Domain.Attendance.CheckinSettings>().IgnoreQueryFilters()
            .AnyAsync(s => s.GymId == gymId, cancellationToken);

    public async Task<bool> IsRemoteTrainingAsync(Guid gymId, CancellationToken cancellationToken = default)
        => await checkinSettings.GetAsync(gymId, cancellationToken) is { RemoteTraining: true };

    public async Task<bool> IsFirstLogEntryAsync(Guid gymId, CancellationToken cancellationToken = default)
        => (await GetRoutineEntryModeAsync(gymId, cancellationToken)).FirstLog;

    public async Task<bool> IsRoutineEntryAsync(Guid gymId, CancellationToken cancellationToken = default)
        => (await GetRoutineEntryModeAsync(gymId, cancellationToken)).RoutineEntry;

    public async Task<RoutineEntryMode> GetRoutineEntryModeAsync(Guid gymId, CancellationToken cancellationToken = default)
    {
        var flags = await gyms.GetFeatureFlagsAsync(gymId, cancellationToken);
        if (flags is null || flags.CheckinQr)
        {
            return new RoutineEntryMode(false, false);
        }

        // Ajustes perezosos: sin fila la entrada con documento está apagada y el conteo es al iniciar.
        var settings = await checkinSettings.GetAsync(gymId, cancellationToken);
        if (settings is { ByDocument: true })
        {
            return new RoutineEntryMode(false, false);
        }

        return new RoutineEntryMode(
            true, settings is { TrainingEntryTrigger: GSM.Domain.Attendance.TrainingEntryTrigger.FirstLog });
    }

    public async Task<OccupancyView> GetOccupancyAsync(Guid gymId, CancellationToken cancellationToken = default)
    {
        var estimate = await OccupancyEstimator.EstimateAsync(
            gymId, clock, gyms, checkinSettings, attendanceRepository, cancellationToken);
        return new OccupancyView(
            estimate.PeopleInside, estimate.Capacity, estimate.Percent, estimate.Level, estimate.AssumedStayMinutes);
    }

    public async Task<RejectedTodayView> GetRejectedTodayAsync(int limit, CancellationToken cancellationToken = default)
    {
        var today = clock.Today;
        var attempts = dbContext.Set<GSM.Domain.Attendance.CheckinAttempt>().Where(a => a.AttemptDate == today);

        var total = await attempts.CountAsync(cancellationToken);
        var unseen = await attempts.CountAsync(a => a.AcknowledgedAt == null, cancellationToken);
        var rows = await attempts
            .OrderByDescending(a => a.AttemptedAt)
            .Take(Math.Clamp(limit, 1, 50))
            .ToListAsync(cancellationToken);

        return new RejectedTodayView(
            total,
            unseen,
            rows.Select(a => new RejectedAttemptView(
                a.Id, a.MemberId, a.AttemptedAt, a.AttemptCount, EnumText.Snake(a.Reason)!, a.AcknowledgedAt is not null)).ToList());
    }

    public async Task<IReadOnlyList<RecentEntryView>> GetRecentEntriesTodayAsync(
        int limit, CancellationToken cancellationToken = default)
    {
        var today = clock.Today;
        var rows = await Attendances
            .Where(a => a.AttendanceDate == today)
            .OrderByDescending(a => a.CheckedInAt)
            .Take(Math.Clamp(limit, 1, 50))
            .Select(a => new { a.Id, a.MemberId, a.CheckedInAt, a.Method })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new RecentEntryView(r.Id, r.MemberId, r.CheckedInAt, r.Method.ToString())).ToList();
    }

    public async Task<IReadOnlyList<HourlyCountView>> GetHourlyCountsAsync(
        int days, CancellationToken cancellationToken = default)
    {
        var today = clock.Today;
        var from = clock.BusinessDayRange(today.AddDays(-(Math.Max(days, 1) - 1))).Start;
        var to = clock.BusinessDayRange(today).End;

        // El filtro global deja solo los ajustes del gym del llamador.
        var remote = await dbContext.Set<GSM.Domain.Attendance.CheckinSettings>()
            .AnyAsync(s => s.RemoteTraining, cancellationToken);

        var instants = await Attendances
            .Where(a => a.CheckedInAt >= from && a.CheckedInAt < to
                        && (!remote || a.Method != GSM.Domain.Attendance.AttendanceMethod.Routine))
            .Select(a => a.CheckedInAt)
            .ToListAsync(cancellationToken);

        return instants
            .GroupBy(i => clock.ToBusinessLocal(i).Hour)
            .OrderBy(g => g.Key)
            .Select(g => new HourlyCountView(g.Key, g.Count()))
            .ToList();
    }
}
