using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Memberships;

/// <summary>
/// Vigencia de un miembro. <see cref="EndDate"/> es el último día cubierto (inclusive).
/// Una renovación es otra <see cref="Membership"/> que arranca al día siguiente de la anterior,
/// de modo que nunca hay dos que cubran la misma fecha.
/// </summary>
public sealed class Membership : BaseEntity, ITenantEntity
{
    private Membership()
    {
    }

    public Guid GymId { get; private set; }

    public Guid MemberId { get; private set; }

    public Guid PlanId { get; private set; }

    public DateOnly StartDate { get; private set; }

    public DateOnly EndDate { get; private set; }

    /// <summary>En almacenamiento solo <see cref="MembershipStatus.Active"/> o <see cref="MembershipStatus.Paused"/>.</summary>
    public MembershipStatus Status { get; private set; } = MembershipStatus.Active;

    public static Membership Create(
        Guid gymId, Guid memberId, Guid planId, DateOnly startDate, int durationDays)
    {
        if (gymId == Guid.Empty || memberId == Guid.Empty || planId == Guid.Empty)
        {
            throw new ValidationDomainException("Datos de membresía incompletos.");
        }

        if (durationDays < 1)
        {
            throw new ValidationDomainException("La duración debe ser de al menos 1 día.");
        }

        return new Membership
        {
            GymId = gymId,
            MemberId = memberId,
            PlanId = planId,
            StartDate = startDate,
            EndDate = startDate.AddDays(durationDays - 1),
            Status = MembershipStatus.Active,
        };
    }

    /// <summary>Estado efectivo en una fecha: materializa <see cref="MembershipStatus.Expired"/>.</summary>
    public MembershipStatus EffectiveStatusOn(DateOnly today)
        => Status == MembershipStatus.Paused
            ? MembershipStatus.Paused
            : EndDate < today
                ? MembershipStatus.Expired
                : MembershipStatus.Active;

    public bool CoversDate(DateOnly date)
        => Status == MembershipStatus.Active && StartDate <= date && date <= EndDate;
}
