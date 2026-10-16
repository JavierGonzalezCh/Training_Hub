using GSM.Application.Classes;
using GSM.Application.Classes.Public;
using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms.Public;
using GSM.Domain.Classes;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Classes;

/// <summary>
/// Lectura que Classes expone a otros módulos. Respeta el Global Query Filter: el llamador siempre
/// opera en su gym.
/// </summary>
public sealed class ClassReader(
    GsmDbContext dbContext,
    IClock clock,
    IGymReader gyms,
    ClassCapacityService capacity) : IClassReader
{
    private IQueryable<ClassBooking> Bookings => dbContext.Set<ClassBooking>();

    public Task<string?> GetClassNameAsync(Guid classId, CancellationToken cancellationToken = default)
        => dbContext.Set<GymClass>().Where(c => c.Id == classId).Select(c => (string?)c.Name)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyCollection<Guid>> GetMemberIdsByClassesAsync(
        IReadOnlyCollection<Guid> classIds, DateOnly since, CancellationToken cancellationToken = default)
    {
        if (classIds.Count == 0)
        {
            return [];
        }

        var query =
            from b in Bookings.Where(b => b.Status == ClassBookingStatus.Booked || b.Status == ClassBookingStatus.Attended)
            join s in dbContext.Set<ClassSession>() on b.SessionId equals s.Id
            where classIds.Contains(s.ClassId) && s.SessionDate >= since
            select b.MemberId;
        return await query.Distinct().ToListAsync(cancellationToken);
    }

    public Task<int> GetAttendedCountAsync(Guid memberId, CancellationToken cancellationToken = default)
        => Bookings.CountAsync(
            b => b.MemberId == memberId && b.Status == ClassBookingStatus.Attended, cancellationToken);

    public async Task<IReadOnlyList<UpcomingBookingView>> GetUpcomingBookingsAsync(
        Guid memberId, int limit, CancellationToken cancellationToken = default)
    {
        var today = clock.Today;

        var query =
            from b in Bookings.Where(b => b.MemberId == memberId && b.Status == ClassBookingStatus.Booked)
            join s in dbContext.Set<ClassSession>() on b.SessionId equals s.Id
            join c in dbContext.Set<GymClass>() on s.ClassId equals c.Id
            where s.SessionDate >= today && s.Status == ClassSessionStatus.Scheduled
            orderby s.SessionDate, s.StartTime
            select new UpcomingBookingView(c.Id, c.Name, s.SessionDate, s.StartTime, s.InstructorName);

        return await query.Take(Math.Clamp(limit, 1, 50)).ToListAsync(cancellationToken);
    }

    public async Task<int> GetTodaySessionCountAsync(Guid gymId, CancellationToken cancellationToken = default)
    {
        var today = clock.Today;

        var defs = await dbContext.Set<GymClass>().ToListAsync(cancellationToken);
        var mat = await dbContext.Set<ClassSession>()
            .Where(s => s.SessionDate == today).ToListAsync(cancellationToken);

        return ClassSchedule.Project(defs, mat, today, today)
            .Count(e => e.Status == ClassSessionStatus.Scheduled.ToString());
    }

    public async Task<IReadOnlyList<TodaySessionView>> GetTodaySessionsAsync(
        Guid gymId, CancellationToken cancellationToken = default)
    {
        var today = clock.Today;

        var defs = await dbContext.Set<GymClass>().ToListAsync(cancellationToken);
        var mat = await dbContext.Set<ClassSession>()
            .Where(s => s.SessionDate == today).ToListAsync(cancellationToken);
        var schedule = await gyms.GetOpeningScheduleAsync(gymId, today, today, cancellationToken);

        var entries = await capacity.EnrichAsync(
            gymId, ClassOpeningHours.Annotate(ClassSchedule.Project(defs, mat, today, today), schedule), cancellationToken);

        var sessionIds = entries.Where(e => e.SessionId is not null).Select(e => e.SessionId!.Value).ToList();
        var booked = sessionIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await Bookings
                .Where(b => sessionIds.Contains(b.SessionId) && b.Status == ClassBookingStatus.Booked)
                .GroupBy(b => b.SessionId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var waiting = sessionIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await dbContext.Set<WaitlistEntry>()
                .Where(w => sessionIds.Contains(w.SessionId)
                            && (w.Status == WaitlistEntryStatus.Waiting || w.Status == WaitlistEntryStatus.Offered))
                .GroupBy(w => w.SessionId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);

        return entries
            .OrderBy(e => e.StartTime)
            .ThenBy(e => e.ClassName)
            .Select(e => new TodaySessionView(
                e.ClassId,
                e.ClassName,
                e.StartTime,
                e.DurationMinutes,
                e.InstructorName,
                e.InstructorUserId,
                e.Status,
                e.EffectiveCapacity ?? e.Capacity,
                e.SessionId is { } id ? booked.GetValueOrDefault(id) : 0,
                e.SessionId is { } wid ? waiting.GetValueOrDefault(wid) : 0,
                e.OutsideOpeningHours))
            .ToList();
    }

    public async Task<IReadOnlyList<WaitlistOfferView>> GetPendingWaitlistOffersAsync(
        Guid memberId, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var today = clock.Today;

        var rows = await (
            from w in dbContext.Set<WaitlistEntry>()
                .Where(w => w.MemberId == memberId && w.Status == WaitlistEntryStatus.Offered && w.OfferExpiresAt > now)
            join s in dbContext.Set<ClassSession>() on w.SessionId equals s.Id
            join c in dbContext.Set<GymClass>() on s.ClassId equals c.Id
            where s.Status == ClassSessionStatus.Scheduled && s.SessionDate >= today
            orderby w.OfferExpiresAt
            select new { c.Id, c.Name, s.SessionDate, s.StartTime, w.OfferExpiresAt })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new WaitlistOfferView(r.Id, r.Name, r.SessionDate, r.StartTime, r.OfferExpiresAt!.Value))
            .ToList();
    }
}
