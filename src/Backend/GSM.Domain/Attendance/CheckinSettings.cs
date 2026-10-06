using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Attendance;

/// <summary>Ajustes de check-in del gym. Uno por gym; si no hay fila, valen los de <see cref="Default"/>.</summary>
public sealed class CheckinSettings : BaseEntity, ITenantEntity
{
    public const int MaxGraceDays = 30;

    private CheckinSettings()
    {
    }

    public Guid GymId { get; private set; }

    public CheckinMode Mode { get; private set; }

    /// <summary>Días tras vencer la membresía en los que la entrada aún se registra (con aviso).</summary>
    public int GraceDays { get; private set; }

    /// <summary>Versión del código fijo. Regenerar la incrementa y el cartel anterior deja de servir.</summary>
    public int StaticCodeVersion { get; private set; }

    public static CheckinSettings Default(Guid gymId)
    {
        if (gymId == Guid.Empty)
        {
            throw new ValidationDomainException("Gym inválido.");
        }

        return new CheckinSettings { GymId = gymId, Mode = CheckinMode.Rotating, GraceDays = 3, StaticCodeVersion = 1 };
    }

    public void Update(CheckinMode mode, int graceDays)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ValidationDomainException("Modo de check-in inválido.");
        }

        if (graceDays is < 0 or > MaxGraceDays)
        {
            throw new ValidationDomainException($"Los días de gracia deben estar entre 0 y {MaxGraceDays}.");
        }

        Mode = mode;
        GraceDays = graceDays;
    }

    public void RegenerateStaticCode() => StaticCodeVersion++;
}
