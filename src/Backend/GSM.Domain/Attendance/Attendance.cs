using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Attendance;

/// <summary>
/// Un registro de entrada al gimnasio. Por regla general una por miembro por día de negocio
/// (<see cref="AttendanceDate"/>); con una <see cref="MemberEntryRule"/> puede haber varias (la
/// validación vive en el caso de uso, no en un índice único). Sin invariantes de negocio: la validación
/// de membresía vigente vive en Application y no bloquea.
/// </summary>
public sealed class Attendance : BaseEntity, ITenantEntity
{
    private Attendance()
    {
    }

    public Guid GymId { get; private set; }

    public Guid MemberId { get; private set; }

    /// <summary>Instante exacto de la entrada (UTC).</summary>
    public DateTimeOffset CheckedInAt { get; private set; }

    /// <summary>Día de negocio de la entrada (zona del gym). Agrupa las entradas del día.</summary>
    public DateOnly AttendanceDate { get; private set; }

    public AttendanceMethod Method { get; private set; }

    /// <summary>Quién dejó pasar al miembro fuera de la gracia ("Dejar pasar" en recepción). Nulo si la entrada no fue autorizada.</summary>
    public Guid? AuthorizedBy { get; private set; }

    /// <summary>Motivo de la entrada autorizada. Solo con <see cref="AuthorizedBy"/>.</summary>
    public string? AuthorizationReason { get; private set; }

    public static Attendance Create(
        Guid gymId, Guid memberId, AttendanceMethod method, DateTimeOffset checkedInAt, DateOnly attendanceDate,
        Guid? authorizedBy = null, string? authorizationReason = null)
    {
        if (gymId == Guid.Empty || memberId == Guid.Empty)
        {
            throw new ValidationDomainException("Datos de asistencia incompletos.");
        }

        return new Attendance
        {
            GymId = gymId,
            MemberId = memberId,
            Method = method,
            CheckedInAt = checkedInAt,
            AttendanceDate = attendanceDate,
            AuthorizedBy = authorizedBy,
            AuthorizationReason = authorizedBy is null ? null : authorizationReason?.Trim(),
        };
    }
}
