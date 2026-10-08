using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Memberships;

/// <summary>
/// Ajustes de membresías del gym: días de gracia y ventana de "por vencer". Uno por gym; si no hay
/// fila valen los de <see cref="Default"/> y no se persisten hasta el primer cambio.
/// </summary>
public sealed class MembershipSettings : BaseEntity, ITenantEntity
{
    public const int DefaultGraceDays = 3;
    public const int DefaultExpiringDays = 7;
    public const int MaxGraceDays = 30;
    public const int MaxExpiringDays = 60;

    private MembershipSettings()
    {
    }

    public Guid GymId { get; private set; }

    /// <summary>Días tras vencer la membresía en los que el miembro aún entra (con letrero).</summary>
    public int GraceDays { get; private set; }

    /// <summary>Con cuántos días o menos para vencer el miembro está "por vencer" y ve el letrero.</summary>
    public int ExpiringDays { get; private set; }

    public static MembershipSettings Default(Guid gymId)
    {
        if (gymId == Guid.Empty)
        {
            throw new ValidationDomainException("Gym inválido.");
        }

        return new MembershipSettings { GymId = gymId, GraceDays = DefaultGraceDays, ExpiringDays = DefaultExpiringDays };
    }

    public void Update(int graceDays, int expiringDays)
    {
        if (graceDays is < 0 or > MaxGraceDays)
        {
            throw new ValidationDomainException($"Los días de gracia deben estar entre 0 y {MaxGraceDays}.");
        }

        if (expiringDays is < 1 or > MaxExpiringDays)
        {
            throw new ValidationDomainException($"Los días de aviso de vencimiento deben estar entre 1 y {MaxExpiringDays}.");
        }

        GraceDays = graceDays;
        ExpiringDays = expiringDays;
    }
}
