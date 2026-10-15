using GSM.Application.Common.Events;
using GSM.Domain.Attendance;

namespace GSM.Application.Attendance.Events;

/// <summary>
/// Se anuló una entrada (corrección de recepción). Memberships (MS5) lo escucha para devolver la entrada
/// de la tiquetera con que se pagó <paramref name="AttendanceId"/>; el manejador debe ser idempotente.
/// La racha y las visitas se recalculan solas (las consultas excluyen lo anulado); los logros ya
/// desbloqueados no se quitan.
/// </summary>
public sealed record CheckInVoided(
    Guid AttendanceId,
    Guid GymId,
    Guid MemberId,
    DateOnly AttendanceDate,
    DateTimeOffset CheckedInAt,
    AttendanceMethod Method,
    Guid VoidedBy,
    DateTimeOffset VoidedAt,
    string Reason) : IDomainEvent;
