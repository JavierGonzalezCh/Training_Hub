using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Routines;

/// <summary>
/// Ajustes de rutinas del gym: cada cuántas semanas se revisa una rutina. Uno por gym; si no hay fila valen los
/// de <see cref="Default"/> y no se persisten hasta el primer cambio (mismo patrón que los demás <c>*_settings</c>).
/// </summary>
public sealed class RoutineSettings : BaseEntity, ITenantEntity
{
    public const int DefaultReviewWeeks = 6;
    public const int MinReviewWeeks = 1;
    public const int MaxReviewWeeks = 52;

    private RoutineSettings()
    {
    }

    public Guid GymId { get; private set; }

    /// <summary>Semanas entre la asignación (o la última revisión) y la fecha de revisión de una rutina.</summary>
    public int ReviewWeeks { get; private set; }

    public static RoutineSettings Default(Guid gymId)
    {
        if (gymId == Guid.Empty)
        {
            throw new ValidationDomainException("Gym inválido.");
        }

        return new RoutineSettings { GymId = gymId, ReviewWeeks = DefaultReviewWeeks };
    }

    public void Update(int reviewWeeks)
    {
        if (reviewWeeks is < MinReviewWeeks or > MaxReviewWeeks)
        {
            throw new ValidationDomainException(
                $"Las semanas de revisión deben estar entre {MinReviewWeeks} y {MaxReviewWeeks}.");
        }

        ReviewWeeks = reviewWeeks;
    }
}
