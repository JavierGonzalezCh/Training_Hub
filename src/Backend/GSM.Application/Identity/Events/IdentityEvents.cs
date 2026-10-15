using GSM.Application.Common.Events;

namespace GSM.Application.Identity.Events;

/// <summary>
/// Una cuenta de equipo dejó de poder ser entrenador asignado en el gym: se desactivó su acceso o perdió
/// el permiso <c>Trainer</c>. Members deja sin entrenador a sus miembros (M1b).
/// </summary>
public sealed record StaffTrainerRoleLost(Guid GymId, Guid UserId) : IDomainEvent;

/// <summary>
/// Un Owner dejó de serlo en el gym (se desactivó su acceso o quedó como staff). Gyms reasigna el contacto de
/// facturación si era él (G4). El handler debe ser idempotente: el evento puede reintentarse.
/// </summary>
public sealed record GymOwnerRemoved(Guid GymId, Guid UserId) : IDomainEvent;
