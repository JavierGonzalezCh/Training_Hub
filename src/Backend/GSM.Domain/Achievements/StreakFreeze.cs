using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Achievements;

/// <summary>
/// Congelador de racha: un imprevisto no arruina un mes de constancia. Vive en <c>achievements</c>
/// —no en Routines— porque aplica también a miembros sin rutina. Se concede uno cada 15 días, con
/// tope de dos disponibles; se consume marcando <see cref="UsedOn"/> con la fecha fallada.
/// </summary>
public sealed class StreakFreeze : BaseEntity, ITenantEntity
{
    private StreakFreeze()
    {
    }

    public Guid GymId { get; private set; }

    public Guid MemberId { get; private set; }

    public DateOnly GrantedOn { get; private set; }

    /// <summary><c>null</c> = disponible.</summary>
    public DateOnly? UsedOn { get; private set; }

    public bool IsAvailable => UsedOn is null;

    public static StreakFreeze Grant(Guid gymId, Guid memberId, DateOnly grantedOn)
    {
        if (gymId == Guid.Empty || memberId == Guid.Empty)
        {
            throw new ValidationDomainException("Datos del congelador incompletos.");
        }

        return new StreakFreeze { GymId = gymId, MemberId = memberId, GrantedOn = grantedOn };
    }

    public void Use(DateOnly failedDate)
    {
        if (UsedOn is not null)
        {
            throw new ConflictException("El congelador ya se usó.");
        }

        UsedOn = failedDate;
    }
}
