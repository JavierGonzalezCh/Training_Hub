using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Achievements;

/// <summary>
/// El puesto final de un participante en un mes cerrado del ranking (días entrenados en el mes). Lo escribe el cierre
/// de mes de la tarea periódica, una vez por gym y mes; los logros de ranking leen el mejor puesto histórico.
/// </summary>
public sealed class RankingMonthResult : BaseEntity, ITenantEntity
{
    private RankingMonthResult()
    {
    }

    public Guid GymId { get; private set; }

    public Guid MemberId { get; private set; }

    /// <summary>Primer día del mes.</summary>
    public DateOnly Month { get; private set; }

    public int Position { get; private set; }

    public int Days { get; private set; }

    public static RankingMonthResult Record(Guid gymId, Guid memberId, DateOnly month, int position, int days)
    {
        if (gymId == Guid.Empty || memberId == Guid.Empty)
        {
            throw new ValidationDomainException("El resultado del ranking necesita gym y miembro.");
        }

        if (month.Day != 1)
        {
            throw new ValidationDomainException("El mes del ranking va por su primer día.");
        }

        if (position < 1 || days < 1)
        {
            throw new ValidationDomainException("El puesto y los días del ranking deben ser positivos.");
        }

        return new RankingMonthResult
        {
            GymId = gymId,
            MemberId = memberId,
            Month = month,
            Position = position,
            Days = days,
        };
    }
}
