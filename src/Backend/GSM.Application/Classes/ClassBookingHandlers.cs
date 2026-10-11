using GSM.Application.Classes.Abstractions;
using GSM.Application.Classes.Contracts;
using GSM.Application.Classes.Events;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Gyms;
using GSM.Application.Gyms.Public;
using GSM.Application.Members.Public;
using GSM.Application.Memberships.Public;
using GSM.Domain.Classes;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Classes;

/// <summary>Utilidades compartidas por los casos de uso de reservas.</summary>
internal static class ClassBookingSupport
{
    public static void EnsureOccurrence(GymClass gymClass, DateOnly date, ClassSession? existing)
    {
        if (existing is null && !ClassSchedule.OccurrencesOf(gymClass, date, date).Any())
        {
            throw new ValidationDomainException("Esa fecha no corresponde a una ocurrencia de la clase.");
        }
    }

    public static void EnsureNotStarted(IClock clock, ClassSession session)
    {
        var startsLocal = session.SessionDate.ToDateTime(session.StartTime);
        if (clock.ToBusinessLocal(clock.UtcNow) >= startsLocal)
        {
            throw new ValidationDomainException("La sesión ya comenzó.");
        }
    }
}

/// <summary>El miembro reserva una plaza. 422 si la clase no tiene cupo; 409 si está llena o cancelada.</summary>
public sealed class BookClassHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    IMemberReader members,
    IMembershipReader memberships,
    IClassRepository classes,
    IClassSessionRepository sessions,
    IClassBookingRepository bookings,
    IUnitOfWork unitOfWork,
    ILogger<BookClassHandler> logger)
{
    public async Task<BookingResponse> HandleAsync(
        Guid classId, DateOnly date, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        await ClassFeatureGate.EnsureMemberModuleEnabledAsync(gyms, gymId, cancellationToken);

        var self = await members.ResolveByUserIdAsync(currentUser.UserId ?? Guid.Empty, cancellationToken)
            ?? throw new ForbiddenDomainException("Tu cuenta no está vinculada a un miembro.");

        var gymClass = await classes.GetAsync(classId, cancellationToken)
            ?? throw new NotFoundException("Clase no encontrada.");

        if (gymClass.Capacity is null)
        {
            throw new ValidationDomainException(
                "Esta clase es de entrada libre: no requiere reserva, basta con asistir.");
        }

        var existing = await sessions.GetAsync(classId, date, cancellationToken);
        ClassBookingSupport.EnsureOccurrence(gymClass, date, existing);

        var session = existing
            ?? await ClassSessionMaterializer.GetOrCreateAsync(sessions, unitOfWork, gymClass, date, cancellationToken);

        if (session.IsCancelled)
        {
            throw new ConflictException("La sesión está cancelada.");
        }

        ClassBookingSupport.EnsureNotStarted(clock, session);

        var mine = await bookings.GetAsync(session.Id, self.Id, cancellationToken);
        if (mine is { IsActive: true })
        {
            return await ResponseAsync(mine, session, classId, self.Id, cancellationToken);
        }

        var active = await bookings.CountActiveAsync(session.Id, cancellationToken);
        if (session.Capacity is { } cap && active >= cap)
        {
            throw new ConflictException("La sesión está completa.");
        }

        if (mine is null)
        {
            mine = ClassBooking.Book(gymId, session.Id, self.Id, clock.UtcNow);
            bookings.Add(mine);
        }
        else
        {
            mine.Rebook(clock.UtcNow);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Miembro {MemberId} reservó la sesión {SessionId} ({ClassId} {Date})", self.Id, session.Id, classId, date);

        return await ResponseAsync(mine, session, classId, self.Id, cancellationToken);
    }

    private async Task<BookingResponse> ResponseAsync(
        ClassBooking booking, ClassSession session, Guid classId, Guid memberId, CancellationToken cancellationToken)
    {
        var membershipExpired = !await memberships.IsActiveAsync(memberId, cancellationToken);
        return new BookingResponse(
            booking.Id, session.Id, classId, session.SessionDate, booking.Status.ToString(), membershipExpired);
    }
}

/// <summary>El miembro cancela su reserva. Permitido hasta la hora de inicio de la sesión.</summary>
public sealed class CancelBookingHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    IMemberReader members,
    IClassSessionRepository sessions,
    IClassBookingRepository bookings,
    IUnitOfWork unitOfWork,
    ILogger<CancelBookingHandler> logger)
{
    public async Task HandleAsync(Guid classId, DateOnly date, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        await ClassFeatureGate.EnsureMemberModuleEnabledAsync(gyms, gymId, cancellationToken);

        var self = await members.ResolveByUserIdAsync(currentUser.UserId ?? Guid.Empty, cancellationToken)
            ?? throw new ForbiddenDomainException("Tu cuenta no está vinculada a un miembro.");

        var session = await sessions.GetAsync(classId, date, cancellationToken)
            ?? throw new NotFoundException("No tienes una reserva en esta sesión.");

        var booking = await bookings.GetAsync(session.Id, self.Id, cancellationToken);
        if (booking is null || !booking.IsActive)
        {
            throw new NotFoundException("No tienes una reserva activa en esta sesión.");
        }

        ClassBookingSupport.EnsureNotStarted(clock, session);

        booking.CancelByMember(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Miembro {MemberId} canceló su reserva en la sesión {SessionId}", self.Id, session.Id);
    }
}

/// <summary>Reservas y asistencia de una sesión concreta (staff).</summary>
public sealed class GetRosterHandler(
    ICurrentUserService currentUser,
    IMemberReader members,
    IClassRepository classes,
    IClassSessionRepository sessions,
    IClassBookingRepository bookings)
{
    public async Task<RosterResponse> HandleAsync(
        Guid classId, DateOnly date, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var gymClass = await classes.GetAsync(classId, cancellationToken)
            ?? throw new NotFoundException("Clase no encontrada.");

        var session = await sessions.GetAsync(classId, date, cancellationToken);
        ClassBookingSupport.EnsureOccurrence(gymClass, date, session);

        var rows = session is null
            ? []
            : await bookings.ListForSessionAsync(session.Id, cancellationToken);

        var entries = new List<RosterEntry>();
        foreach (var b in rows.OrderBy(b => b.BookedAt))
        {
            var member = await members.GetAsync(b.MemberId, cancellationToken);
            entries.Add(new RosterEntry(
                b.Id, b.MemberId,
                member is null ? "(miembro eliminado)" : $"{member.FirstName} {member.LastName}",
                b.Status.ToString(), b.BookedAt, b.AttendedAt));
        }

        return new RosterResponse(
            gymClass.Id, gymClass.Name, date,
            session?.StartTime ?? gymClass.StartTime,
            session?.Capacity ?? gymClass.Capacity,
            (session?.Status ?? ClassSessionStatus.Scheduled).ToString(),
            rows.Count(b => b.Status == ClassBookingStatus.Booked),
            entries);
    }
}

/// <summary>El staff marca asistentes y ausentes de una sesión. Publica <c>ClassAttendanceRegistered</c> por cada asistente.</summary>
public sealed class MarkAttendanceHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    IMemberReader members,
    IClassRepository classes,
    IClassSessionRepository sessions,
    IClassBookingRepository bookings,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    GetRosterHandler roster,
    ILogger<MarkAttendanceHandler> logger)
{
    public async Task<RosterResponse> HandleAsync(
        Guid classId, DateOnly date, MarkAttendanceRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();

        var gymClass = await classes.GetAsync(classId, cancellationToken)
            ?? throw new NotFoundException("Clase no encontrada.");

        var existing = await sessions.GetAsync(classId, date, cancellationToken);
        ClassBookingSupport.EnsureOccurrence(gymClass, date, existing);

        var session = existing
            ?? await ClassSessionMaterializer.GetOrCreateAsync(sessions, unitOfWork, gymClass, date, cancellationToken);

        if (session.IsCancelled)
        {
            throw new ConflictException("La sesión está cancelada.");
        }

        var attended = (request.Attended ?? []).Distinct().ToList();
        var noShow = (request.NoShow ?? []).Distinct().Except(attended).ToList();

        // Asistir a una clase cuenta como entrada del día: con el gym cerrado (gym_closed) no se registra,
        // sin excepción. Se mide a la hora de inicio de la sesión, no a la hora en que el staff la marca.
        if (attended.Count > 0)
        {
            var status = await gyms.GetOpenStatusAtLocalAsync(gymId, session.SessionDate, session.StartTime, cancellationToken);
            if (!status.IsOpen)
            {
                throw new ConflictException(OpenStatusText.ClosedMessage(status, clock.Today));
            }
        }
        var now = clock.UtcNow;
        var current = await bookings.ListForSessionAsync(session.Id, cancellationToken);
        var byMember = current.ToDictionary(b => b.MemberId);

        var attendedForEvents = new List<Guid>();

        foreach (var memberId in attended)
        {
            if (!await members.ExistsAsync(memberId, cancellationToken))
            {
                throw new NotFoundException($"Miembro {memberId} no encontrado.");
            }

            if (byMember.TryGetValue(memberId, out var booking))
            {
                booking.MarkAttended(now);
            }
            else
            {
                bookings.Add(ClassBooking.AttendDirectly(gymId, session.Id, memberId, now));
            }

            attendedForEvents.Add(memberId);
        }

        foreach (var memberId in noShow)
        {
            if (byMember.TryGetValue(memberId, out var booking) && booking.Status == ClassBookingStatus.Booked)
            {
                booking.MarkNoShow();
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Sesión {SessionId}: {Attended} asistentes, {NoShow} ausentes marcados",
            session.Id, attended.Count, noShow.Count);

        foreach (var memberId in attendedForEvents)
        {
            await events.PublishAsync(
                new ClassAttendanceRegistered(memberId, gymId, session.SessionDate), cancellationToken);
        }

        return await roster.HandleAsync(classId, date, cancellationToken);
    }
}

/// <summary>Histórico de clases del miembro (todas sus reservas y asistencias).</summary>
public sealed class GetMyClassHistoryHandler(
    ICurrentUserService currentUser,
    IGymReader gyms,
    IMemberReader members,
    IClassBookingRepository bookings)
{
    public async Task<IReadOnlyList<ClassHistoryItem>> HandleAsync(CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        await ClassFeatureGate.EnsureMemberModuleEnabledAsync(gyms, gymId, cancellationToken);

        var self = await members.ResolveByUserIdAsync(currentUser.UserId ?? Guid.Empty, cancellationToken)
            ?? throw new ForbiddenDomainException("Tu cuenta no está vinculada a un miembro.");

        var rows = await bookings.ListForMemberAsync(self.Id, from: null, to: null, cancellationToken);

        return rows
            .OrderByDescending(r => r.Session.SessionDate)
            .ThenByDescending(r => r.Session.StartTime)
            .Select(r => new ClassHistoryItem(
                r.Booking.Id, r.Class.Id, r.Class.Name,
                r.Session.SessionDate, r.Session.StartTime, r.Booking.Status.ToString()))
            .ToList();
    }
}
