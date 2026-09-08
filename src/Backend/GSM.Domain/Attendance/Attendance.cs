using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Attendance;

/// <summary>
/// Un registro de entrada al gimnasio. Como mucho uno por miembro por día de negocio
/// (<see cref="AttendanceDate"/> con índice único). Sin invariantes de negocio: la validación
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

    /// <summary>Día de negocio de la entrada (zona del gym). Clave de la unicidad "una por día".</summary>
    public DateOnly AttendanceDate { get; private set; }

    public AttendanceMethod Method { get; private set; }

    public static Attendance Create(
        Guid gymId, Guid memberId, AttendanceMethod method, DateTimeOffset checkedInAt, DateOnly attendanceDate)
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
        };
    }
}
