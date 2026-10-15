using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Classes;

/// <summary>
/// Bloqueo para reservar clases por inasistencias (Classes v2, C3). Lo crea el sistema al alcanzar el
/// límite de no-shows; el staff puede levantarlo con motivo. Las reservas ya hechas se mantienen.
/// Las filas no se borran: son la historia de la penalización y marcan desde cuándo se vuelven a contar
/// las inasistencias.
/// </summary>
public sealed class BookingBlock : BaseEntity, ITenantEntity
{
    private BookingBlock()
    {
    }

    public Guid GymId { get; private set; }

    public Guid MemberId { get; private set; }

    /// <summary>Último día en que no puede reservar (inclusive).</summary>
    public DateOnly BlockedUntil { get; private set; }

    /// <summary>Inasistencias contadas cuando se aplicó.</summary>
    public int StrikeCount { get; private set; }

    /// <summary>Instante (de <c>IClock</c>) en que se aplicó; desde aquí se vuelven a contar las inasistencias.</summary>
    public DateTimeOffset AppliedAt { get; private set; }

    public DateTimeOffset? LiftedAt { get; private set; }

    public Guid? LiftedByUserId { get; private set; }

    public string? LiftReason { get; private set; }

    public bool IsActiveOn(DateOnly today) => LiftedAt is null && BlockedUntil >= today;

    public static BookingBlock Apply(Guid gymId, Guid memberId, DateOnly blockedUntil, int strikeCount, DateTimeOffset now)
    {
        if (gymId == Guid.Empty || memberId == Guid.Empty)
        {
            throw new ValidationDomainException("Datos de bloqueo incompletos.");
        }

        return new BookingBlock
        {
            GymId = gymId,
            MemberId = memberId,
            BlockedUntil = blockedUntil,
            StrikeCount = strikeCount,
            AppliedAt = now,
        };
    }

    public void Lift(Guid? userId, string reason, DateTimeOffset now, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationDomainException("Indica el motivo para levantar la penalización.");
        }

        if (!IsActiveOn(today))
        {
            throw new ConflictException("La penalización ya no está vigente.");
        }

        LiftedAt = now;
        LiftedByUserId = userId;
        LiftReason = reason.Trim();
    }
}
