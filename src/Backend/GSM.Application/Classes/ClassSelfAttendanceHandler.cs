using GSM.Application.Attendance.Public;
using GSM.Application.Classes.Abstractions;
using GSM.Application.Classes.Contracts;
using GSM.Application.Classes.Events;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Gyms.Public;
using GSM.Application.Members.Public;
using GSM.Application.Memberships.Public;
using GSM.Domain.Classes;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Classes;

/// <summary>
/// C7: el miembro marca su propia asistencia ("Estoy en clase"). Funciona solo si ya registró su entrada al gym
/// hoy (eso prueba que está en el gym) y estamos entre 15 minutos antes y 15 minutos después del inicio de la
/// clase. Con cupo exige una reserva activa; sin cupo basta con asistir. Pasa por las mismas verificaciones que
/// el staff (gym abierto, al día, sin "Dejar pasar"). No registra otra entrada: la del día ya existe.
/// Idempotente: marcar dos veces devuelve lo mismo y no publica nada la segunda vez.
/// </summary>
public sealed class MarkOwnAttendanceHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    IMemberReader members,
    IMembershipReader memberships,
    IAttendanceReader attendance,
    IClassRepository classes,
    IClassSessionRepository sessions,
    IClassBookingRepository bookings,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<MarkOwnAttendanceHandler> logger)
{
    /// <summary>Minutos antes y después del inicio de la clase en que se puede marcar.</summary>
    public const int WindowMinutes = 15;

    public async Task<SelfAttendanceResponse> HandleAsync(
        Guid classId, DateOnly date, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        await ClassFeatureGate.EnsureMemberModuleEnabledAsync(gyms, gymId, cancellationToken);

        var self = await members.ResolveByUserIdAsync(currentUser.UserId ?? Guid.Empty, cancellationToken)
            ?? throw new ForbiddenDomainException("Tu cuenta no está vinculada a un miembro.");

        var gymClass = await classes.GetAsync(classId, cancellationToken)
            ?? throw new NotFoundException("Clase no encontrada.");

        var existing = await sessions.GetAsync(classId, date, cancellationToken);
        ClassBookingSupport.EnsureOccurrence(gymClass, date, existing);

        if (existing is { IsCancelled: true })
        {
            throw new ConflictException("La sesión está cancelada.");
        }

        var current = existing is null ? null : await bookings.GetAsync(existing.Id, self.Id, cancellationToken);
        if (current is { Status: ClassBookingStatus.Attended })
        {
            return Response(current, existing!, classId);
        }

        // Ventana de tiempo: de 15 minutos antes a 15 minutos después del inicio.
        var start = existing?.StartTime ?? gymClass.StartTime;
        var startsLocal = date.ToDateTime(start);
        var nowLocal = clock.ToBusinessLocal(clock.UtcNow);
        if (nowLocal < startsLocal.AddMinutes(-WindowMinutes) || nowLocal > startsLocal.AddMinutes(WindowMinutes))
        {
            throw new ValidationDomainException(
                $"Solo puedes marcar \"Estoy en clase\" entre {WindowMinutes} minutos antes y {WindowMinutes} minutos "
                + $"después del inicio ({start:HH\\:mm}).");
        }

        // Con cupo hace falta una reserva activa; sin cupo basta con asistir.
        if (gymClass.Capacity is not null && current is not { Status: ClassBookingStatus.Booked })
        {
            throw new ConflictException("Necesitas una reserva activa en esta sesión para marcar tu asistencia.");
        }

        if (current is { Status: ClassBookingStatus.NoShow or ClassBookingStatus.Cancelled })
        {
            throw new ConflictException("Tu reserva en esta sesión ya no está activa; habla con recepción.");
        }

        await ClassOpeningHours.EnsureOccurrenceOpenAsync(
            gyms, clock, gymId, date, start, existing?.DurationMinutes ?? gymClass.DurationMinutes, cancellationToken);
        await ClassAccess.EnsureCoveredAsync(
            memberships, self.Id, date, "asistir a esta clase", cancellationToken);

        // Debe haber registrado su entrada al gym hoy: es la prueba de que está en el gym.
        var visitDates = await attendance.GetAttendanceDatesAsync(self.Id, date, cancellationToken);
        if (!visitDates.Contains(date))
        {
            throw new ConflictException("Registra tu entrada al gimnasio primero para marcar tu asistencia a la clase.");
        }

        var session = existing
            ?? await ClassSessionMaterializer.GetOrCreateAsync(sessions, unitOfWork, gymClass, date, cancellationToken);

        var now = clock.UtcNow;
        if (current is null)
        {
            current = ClassBooking.AttendDirectly(gymId, session.Id, self.Id, now);
            bookings.Add(current);
        }
        else
        {
            current.MarkAttended(now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Miembro {MemberId} marcó su asistencia a la sesión {SessionId} ({ClassId} {Date})",
            self.Id, session.Id, classId, date);

        // Logros de tipo "clases" (el check-in del día ya existe, así que Attendance no duplica) y recordatorio.
        await events.PublishAsync(new ClassAttendanceRegistered(self.Id, gymId, date), cancellationToken);
        await events.PublishAsync(new ClassSelfAttended(gymId, self.Id, classId, date), cancellationToken);

        return Response(current, session, classId);
    }

    private static SelfAttendanceResponse Response(ClassBooking booking, ClassSession session, Guid classId)
        => new(booking.Id, session.Id, classId, session.SessionDate, booking.Status.ToString(), booking.AttendedAt);
}
