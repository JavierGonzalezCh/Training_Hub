using GSM.Application.Classes;
using GSM.Application.Classes.Public;
using GSM.Application.Common.Abstractions;
using GSM.Domain.Classes;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Classes;

/// <summary>
/// Lectura que Classes expone a otros módulos. Respeta el Global Query Filter: el llamador siempre
/// opera en su gym.
/// </summary>
public sealed class ClassReader(GsmDbContext dbContext, IClock clock) : IClassReader
{
    private IQueryable<ClassBooking> Bookings => dbContext.Set<ClassBooking>();

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
}
