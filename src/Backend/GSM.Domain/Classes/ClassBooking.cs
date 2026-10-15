using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Classes;

/// <summary>
/// Reserva y asistencia de un miembro a una <c>ClassSession</c>: una sola fila por miembro y sesión
/// (índice único <c>(session_id, member_id)</c>). Reservar de nuevo tras cancelar reactiva la
/// misma fila. En una clase sin cupo no hay reserva previa: marcar la asistencia crea la fila
/// directamente en <see cref="ClassBookingStatus.Attended"/>.
/// </summary>
public sealed class ClassBooking : BaseEntity, ITenantEntity
{
    private ClassBooking()
    {
    }

    public Guid GymId { get; private set; }

    public Guid SessionId { get; private set; }

    public Guid MemberId { get; private set; }

    public ClassBookingStatus Status { get; private set; }

    public DateTimeOffset? BookedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public DateTimeOffset? AttendedAt { get; private set; }

    /// <summary>
    /// C3: instante en que esta reserva pasó a contar como inasistencia (marcada "no vino" por el staff o
    /// cancelada después del plazo). Nulo si no cuenta. Se limpia si luego se marca la asistencia; una
    /// reserva que nadie marcó nunca lo recibe.
    /// </summary>
    public DateTimeOffset? StrikeAt { get; private set; }

    public bool IsActive => Status == ClassBookingStatus.Booked;

    public static ClassBooking Book(Guid gymId, Guid sessionId, Guid memberId, DateTimeOffset now)
    {
        if (gymId == Guid.Empty || sessionId == Guid.Empty || memberId == Guid.Empty)
        {
            throw new ValidationDomainException("Datos de reserva incompletos.");
        }

        return new ClassBooking
        {
            GymId = gymId,
            SessionId = sessionId,
            MemberId = memberId,
            Status = ClassBookingStatus.Booked,
            BookedAt = now,
        };
    }

    /// <summary>Crea la fila directamente en <see cref="ClassBookingStatus.Attended"/> (clase sin cupo, o alta manual del staff).</summary>
    public static ClassBooking AttendDirectly(Guid gymId, Guid sessionId, Guid memberId, DateTimeOffset now)
    {
        var booking = Book(gymId, sessionId, memberId, now);
        booking.Status = ClassBookingStatus.Attended;
        booking.AttendedAt = now;
        return booking;
    }

    /// <summary>Reactiva una reserva cancelada. No hace nada si ya está reservada.</summary>
    public void Rebook(DateTimeOffset now)
    {
        if (Status is ClassBookingStatus.Attended or ClassBookingStatus.NoShow)
        {
            throw new ConflictException("La sesión ya se realizó; no se puede volver a reservar.");
        }

        Status = ClassBookingStatus.Booked;
        BookedAt = now;
        CancelledAt = null;
    }

    /// <summary>El miembro cancela su reserva. Solo si está reservada. <paramref name="late"/>: después del plazo, cuenta como inasistencia.</summary>
    public void CancelByMember(DateTimeOffset now, bool late = false)
    {
        if (Status != ClassBookingStatus.Booked)
        {
            throw new ConflictException("No tienes una reserva activa en esta sesión.");
        }

        Status = ClassBookingStatus.Cancelled;
        CancelledAt = now;
        if (late)
        {
            StrikeAt = now;
        }
    }

    /// <summary>Cancelación arrastrada por la cancelación de la sesión.</summary>
    public void CancelBySessionCancellation(DateTimeOffset now)
    {
        if (Status != ClassBookingStatus.Booked)
        {
            return;
        }

        Status = ClassBookingStatus.Cancelled;
        CancelledAt = now;
    }

    public void MarkAttended(DateTimeOffset now)
    {
        Status = ClassBookingStatus.Attended;
        AttendedAt = now;
        StrikeAt = null;
    }

    public void MarkNoShow(DateTimeOffset now)
    {
        if (Status != ClassBookingStatus.Booked)
        {
            throw new ConflictException("Solo se puede marcar ausente a quien tenía reserva.");
        }

        Status = ClassBookingStatus.NoShow;
        StrikeAt = now;
    }
}
