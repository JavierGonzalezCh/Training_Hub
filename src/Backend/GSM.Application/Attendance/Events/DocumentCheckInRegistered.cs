using GSM.Application.Common.Events;

namespace GSM.Application.Attendance.Events;

/// <summary>
/// Se registró una entrada con el documento digitado en el kiosco (A4). Notifications avisa al miembro dueño del
/// documento (A7, spec Attendance §7); el manejador debe ser idempotente.
/// </summary>
public sealed record DocumentCheckInRegistered(
    Guid AttendanceId, Guid GymId, Guid MemberId, DateTimeOffset CheckedInAt) : IDomainEvent;
