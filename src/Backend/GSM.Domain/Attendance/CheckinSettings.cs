using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Attendance;

/// <summary>
/// Ajustes de check-in del gym. Uno por gym; si no hay fila, valen los de <see cref="Default"/>.
/// Los días de gracia ya no viven aquí: pasaron a <c>memberships.membership_settings</c> (MS1).
/// </summary>
public sealed class CheckinSettings : BaseEntity, ITenantEntity
{
    private CheckinSettings()
    {
    }

    public Guid GymId { get; private set; }

    public CheckinMode Mode { get; private set; }

    /// <summary>Versión del código fijo. Regenerar la incrementa y el cartel anterior deja de servir.</summary>
    public int StaticCodeVersion { get; private set; }

    public static CheckinSettings Default(Guid gymId)
    {
        if (gymId == Guid.Empty)
        {
            throw new ValidationDomainException("Gym inválido.");
        }

        return new CheckinSettings { GymId = gymId, Mode = CheckinMode.Rotating, StaticCodeVersion = 1 };
    }

    public void Update(CheckinMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ValidationDomainException("Modo de check-in inválido.");
        }

        Mode = mode;
    }

    public void RegenerateStaticCode() => StaticCodeVersion++;
}
