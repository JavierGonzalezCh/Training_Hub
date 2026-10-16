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

    /// <summary>Cuándo se anuló la entrada (corrección de recepción). Nulo si sigue vigente.</summary>
    public DateTimeOffset? VoidedAt { get; private set; }

    public Guid? VoidedBy { get; private set; }

    public string? VoidReason { get; private set; }

    public bool IsVoided => VoidedAt is not null;

    /// <summary>
    /// Comprobación de ubicación con el QR fijo (A6). Nulo = no se pidió. Solo el resultado; nunca las coordenadas.
    /// </summary>
    public LocationCheck? LocationCheck { get; private set; }

    /// <summary>
    /// ¿Cuenta para logros y racha? No cuenta si el miembro estaba fuera del radio o no compartió su ubicación
    /// (sigue contando para el acceso, la tiquetera y la ocupación).
    /// </summary>
    public bool CountsForProgress => LocationCheck is null or GSM.Domain.Attendance.LocationCheck.Inside;

    /// <summary>Borrado lógico con motivo obligatorio: la fila queda como traza y deja de contar.</summary>
    public void Void(Guid voidedBy, string reason, DateTimeOffset now)
    {
        if (IsVoided)
        {
            throw new ConflictException("La entrada ya está anulada.");
        }

        var trimmed = (reason ?? string.Empty).Trim();
        if (trimmed.Length is < 3 or > 500)
        {
            throw new ValidationDomainException("Indica el motivo de la anulación (3 a 500 caracteres).");
        }

        VoidedAt = now;
        VoidedBy = voidedBy;
        VoidReason = trimmed;
    }

    public static Attendance Create(
        Guid gymId, Guid memberId, AttendanceMethod method, DateTimeOffset checkedInAt, DateOnly attendanceDate,
        Guid? authorizedBy = null, string? authorizationReason = null, LocationCheck? locationCheck = null)
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
            LocationCheck = locationCheck,
        };
    }
}
