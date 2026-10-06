using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Attendance;

/// <summary>
/// Intento de entrada rechazado (membresía vencida fuera de la gracia o inexistente). Un miembro
/// que insiste el mismo día repite la misma fila (<see cref="AttemptCount"/>) mientras siga pendiente.
/// </summary>
public sealed class CheckinAttempt : BaseEntity, ITenantEntity
{
    private CheckinAttempt()
    {
    }

    public Guid GymId { get; private set; }

    public Guid MemberId { get; private set; }

    public CheckinRejectionReason Reason { get; private set; }

    public DateTimeOffset AttemptedAt { get; private set; }

    /// <summary>Día de negocio del intento. Clave de la deduplicación.</summary>
    public DateOnly AttemptDate { get; private set; }

    public int AttemptCount { get; private set; }

    public DateOnly? MembershipEndedOn { get; private set; }

    public DateTimeOffset? AcknowledgedAt { get; private set; }

    public Guid? AcknowledgedBy { get; private set; }

    public static CheckinAttempt Create(
        Guid gymId, Guid memberId, CheckinRejectionReason reason,
        DateTimeOffset attemptedAt, DateOnly attemptDate, DateOnly? membershipEndedOn)
    {
        if (gymId == Guid.Empty || memberId == Guid.Empty)
        {
            throw new ValidationDomainException("Datos del intento incompletos.");
        }

        return new CheckinAttempt
        {
            GymId = gymId,
            MemberId = memberId,
            Reason = reason,
            AttemptedAt = attemptedAt,
            AttemptDate = attemptDate,
            AttemptCount = 1,
            MembershipEndedOn = membershipEndedOn,
        };
    }

    public void Repeat(DateTimeOffset now)
    {
        AttemptCount++;
        AttemptedAt = now;
    }

    public void Acknowledge(Guid userId, DateTimeOffset now)
    {
        if (AcknowledgedAt is not null)
        {
            return;
        }

        AcknowledgedAt = now;
        AcknowledgedBy = userId;
    }
}
