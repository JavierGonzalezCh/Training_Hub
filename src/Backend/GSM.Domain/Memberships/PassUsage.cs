using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Memberships;

/// <summary>
/// Una entrada de una tiquetera ya consumida (MS5, Memberships v2 §7). Una fila por entrada de asistencia
/// (índice único por <see cref="AttendanceId"/>, así consumir es idempotente); una entrada anulada
/// elimina la fila y la entrada se devuelve. El saldo es <c>EntriesTotal</c> menos las filas.
/// </summary>
public sealed class PassUsage : BaseEntity, ITenantEntity
{
    private PassUsage()
    {
    }

    public Guid GymId { get; private set; }

    public Guid MembershipId { get; private set; }

    /// <summary>La entrada de asistencia que consumió (sin FK: es de otro módulo).</summary>
    public Guid AttendanceId { get; private set; }

    /// <summary>Día de negocio de la entrada.</summary>
    public DateOnly UsedOn { get; private set; }

    public static PassUsage Create(Guid gymId, Guid membershipId, Guid attendanceId, DateOnly usedOn)
    {
        if (gymId == Guid.Empty || membershipId == Guid.Empty || attendanceId == Guid.Empty)
        {
            throw new ValidationDomainException("Datos de consumo de tiquetera incompletos.");
        }

        return new PassUsage { GymId = gymId, MembershipId = membershipId, AttendanceId = attendanceId, UsedOn = usedOn };
    }
}
