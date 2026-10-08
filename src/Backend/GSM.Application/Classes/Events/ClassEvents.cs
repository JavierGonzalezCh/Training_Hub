using GSM.Application.Common.Events;

namespace GSM.Application.Classes.Events;

/// <summary>
/// Publicado al marcar la asistencia de un miembro a una sesión de clase. Un handler en Attendance
/// registra el check-in del día (idempotente por día); Achievements evalúa los logros de tipo
/// <c>classes</c>. Reacción, no parte de la respuesta.
/// </summary>
public sealed record ClassAttendanceRegistered(
    Guid MemberId, Guid GymId, DateOnly SessionDate) : IDomainEvent;

/// <summary>
/// Publicado al cancelar una ocurrencia puntual. Hoy sin consumidores; cuando exista push, es el
/// disparador natural del aviso a los miembros afectados.
/// </summary>
public sealed record ClassSessionCancelled(
    Guid ClassId, Guid GymId, DateOnly SessionDate, IReadOnlyList<Guid> AffectedMemberIds) : IDomainEvent;
