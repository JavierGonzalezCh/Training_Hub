using GSM.Domain.Classes;

namespace GSM.Application.Classes.Abstractions;

/// <summary>Acceso a <see cref="ClassBooking"/>. Global Query Filter por gym. Sin <c>SaveChanges</c>.</summary>
public interface IClassBookingRepository
{
    Task<ClassBooking?> GetAsync(Guid sessionId, Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>Reservas de una sesión (todos los estados), para el roster y para arrastrar la cancelación.</summary>
    Task<IReadOnlyList<ClassBooking>> ListForSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>Nº de reservas en estado <c>booked</c> de la sesión (control de cupo).</summary>
    Task<int> CountActiveAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>Reservas de un socio en un rango de fechas de sesión, con la sesión y la clase resueltas.</summary>
    Task<IReadOnlyList<(ClassBooking Booking, ClassSession Session, GymClass Class)>> ListForMemberAsync(
        Guid memberId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default);

    /// <summary>Nº de sesiones distintas a las que el socio asistió (<c>attended</c>).</summary>
    Task<int> CountAttendedAsync(Guid memberId, CancellationToken cancellationToken = default);

    void Add(ClassBooking booking);
}
