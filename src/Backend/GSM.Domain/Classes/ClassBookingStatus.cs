namespace GSM.Domain.Classes;

/// <summary>
/// Estado de la reserva/asistencia de un socio a una sesión. Enum nativo de Postgres
/// (<c>classes.class_booking_status</c>). Reserva y asistencia son estados del mismo hecho.
/// </summary>
public enum ClassBookingStatus
{
    Booked,
    Cancelled,
    Attended,
    NoShow,
}
