using GSM.Application.Common.Events;

namespace GSM.Application.Identity.Events;

/// <summary>
/// Una cuenta de equipo dejó de poder ser entrenador asignado en el gym: se desactivó su acceso o perdió
/// el permiso <c>Trainer</c>. Members deja sin entrenador a sus miembros (M1b).
/// </summary>
public sealed record StaffTrainerRoleLost(Guid GymId, Guid UserId) : IDomainEvent;
