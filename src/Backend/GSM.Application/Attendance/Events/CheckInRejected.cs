using GSM.Application.Common.Events;
using GSM.Domain.Attendance;

namespace GSM.Application.Attendance.Events;

/// <summary>
/// Se rechazó una entrada. Notifications crea el aviso de acción para el staff (una fila por gym). Si el
/// miembro insiste mientras el intento sigue sin verse, se vuelve a publicar con el contador al día.
/// </summary>
public sealed record CheckInRejected(
    Guid AttemptId,
    Guid GymId,
    Guid MemberId,
    CheckinRejectionReason Reason,
    int AttemptCount,
    DateOnly? MembershipEndedOn) : IDomainEvent;
