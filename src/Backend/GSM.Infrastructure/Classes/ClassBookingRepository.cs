using GSM.Application.Classes.Abstractions;
using GSM.Domain.Classes;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Classes;

/// <summary><see cref="ClassBooking"/> es <c>ITenantEntity</c>: el Global Query Filter ya restringe al gym actual.</summary>
public sealed class ClassBookingRepository(GsmDbContext dbContext) : IClassBookingRepository
{
    private DbSet<ClassBooking> Bookings => dbContext.Set<ClassBooking>();

    public Task<ClassBooking?> GetAsync(Guid sessionId, Guid memberId, CancellationToken cancellationToken = default)
        => Bookings.FirstOrDefaultAsync(
            b => b.SessionId == sessionId && b.MemberId == memberId, cancellationToken);

    public async Task<IReadOnlyList<ClassBooking>> ListForSessionAsync(
        Guid sessionId, CancellationToken cancellationToken = default)
        => await Bookings.Where(b => b.SessionId == sessionId).ToListAsync(cancellationToken);

    public Task<int> CountActiveAsync(Guid sessionId, CancellationToken cancellationToken = default)
        => Bookings.CountAsync(
            b => b.SessionId == sessionId && b.Status == ClassBookingStatus.Booked, cancellationToken);

    public async Task<IReadOnlyList<(ClassBooking Booking, ClassSession Session, GymClass Class)>> ListForMemberAsync(
        Guid memberId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        var query =
            from b in Bookings.Where(b => b.MemberId == memberId)
            join s in dbContext.Set<ClassSession>() on b.SessionId equals s.Id
            join c in dbContext.Set<GymClass>() on s.ClassId equals c.Id
            select new { b, s, c };

        if (from is { } f)
        {
            query = query.Where(x => x.s.SessionDate >= f);
        }

        if (to is { } t)
        {
            query = query.Where(x => x.s.SessionDate <= t);
        }

        var rows = await query.ToListAsync(cancellationToken);
        return rows.Select(x => (x.b, x.s, x.c)).ToList();
    }

    public Task<int> CountAttendedAsync(Guid memberId, CancellationToken cancellationToken = default)
        => Bookings.CountAsync(
            b => b.MemberId == memberId && b.Status == ClassBookingStatus.Attended, cancellationToken);

    public async Task<int> CountFutureActiveForMemberAsync(
        Guid memberId, DateOnly today, TimeOnly nowTime, CancellationToken cancellationToken = default)
        => await (
            from b in Bookings.Where(b => b.MemberId == memberId && b.Status == ClassBookingStatus.Booked)
            join s in dbContext.Set<ClassSession>() on b.SessionId equals s.Id
            where s.SessionDate > today || (s.SessionDate == today && s.StartTime > nowTime)
            select b.Id).CountAsync(cancellationToken);

    public Task<int> CountStrikesSinceAsync(Guid memberId, DateTimeOffset since, CancellationToken cancellationToken = default)
        => Bookings.CountAsync(b => b.MemberId == memberId && b.StrikeAt != null && b.StrikeAt > since, cancellationToken);

    public void Add(ClassBooking booking) => Bookings.Add(booking);
}
