using GSM.Domain.Gyms;
using GSM.Domain.Memberships;

namespace GSM.Application.Memberships;

/// <summary>Valores iniciales de membresías por tipo de negocio (docs/GSM-tipos-de-negocio.md §4.3).</summary>
public sealed record MembershipTemplate(int GraceDays, int ExpiringDays, decimal EnrollmentFee);

public static class MembershipTemplates
{
    /// <summary>
    /// El estudio vende sobre todo tiqueteras, que no tienen gracia, y deja sus mensuales igual (0). El entrenador
    /// personal cobra a mano y con menos rigor (5).
    /// </summary>
    public static MembershipTemplate For(GymType type) => type switch
    {
        GymType.Studio => new(0, MembershipSettings.DefaultExpiringDays, 0m),
        GymType.PersonalTrainer => new(5, MembershipSettings.DefaultExpiringDays, 0m),
        _ => new(MembershipSettings.DefaultGraceDays, MembershipSettings.DefaultExpiringDays, 0m),
    };
}
