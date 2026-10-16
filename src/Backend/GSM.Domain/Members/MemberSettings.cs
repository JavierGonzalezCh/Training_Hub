using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Members;

/// <summary>
/// Ajustes de Members del gym (M6): el plazo de anonimización. Uno por gym; si no hay fila valen los de
/// <see cref="Default"/> y no se persisten hasta el primer cambio.
/// </summary>
public sealed class MemberSettings : BaseEntity, ITenantEntity
{
    public const int DefaultAnonymizationMonths = 24;
    public const int MinAnonymizationMonths = 6;
    public const int MaxAnonymizationMonths = 120;

    /// <summary>Días de anticipación con que se avisa a los Owners (fijo, no configurable).</summary>
    public const int NoticeDays = 7;

    private MemberSettings()
    {
    }

    public Guid GymId { get; private set; }

    /// <summary>Meses desde la baja tras los cuales la ficha se anonimiza.</summary>
    public int AnonymizationMonths { get; private set; }

    public static MemberSettings Default(Guid gymId)
    {
        if (gymId == Guid.Empty)
        {
            throw new ValidationDomainException("Gym inválido.");
        }

        return new MemberSettings { GymId = gymId, AnonymizationMonths = DefaultAnonymizationMonths };
    }

    public void Update(int anonymizationMonths)
    {
        if (anonymizationMonths is < MinAnonymizationMonths or > MaxAnonymizationMonths)
        {
            throw new ValidationDomainException(
                $"El plazo de anonimización debe estar entre {MinAnonymizationMonths} y {MaxAnonymizationMonths} meses.");
        }

        AnonymizationMonths = anonymizationMonths;
    }
}
