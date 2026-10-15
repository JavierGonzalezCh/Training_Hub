using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Routines;

/// <summary>
/// Alternativa recomendada entre dos ejercicios: "en lugar de uno puedes hacer el otro". La relación vale en los dos
/// sentidos y se guarda una sola vez, con el par ordenado (<see cref="ExerciseId"/> menor que
/// <see cref="AlternativeId"/> por su texto en minúsculas, que es también el orden de <c>uuid</c> en Postgres).
/// </summary>
/// <remarks>
/// <see cref="GymId"/> <c>null</c> = alternativa del sistema (entre ejercicios del sistema); con valor = la definió
/// ese gym. Como <see cref="Exercise"/>, no implementa <see cref="ITenantEntity"/>: el filtrado
/// <c>gym_id IS NULL OR gym_id = @current</c> va explícito en el repositorio.
/// </remarks>
public sealed class ExerciseAlternative : BaseEntity
{
    private ExerciseAlternative()
    {
    }

    public Guid? GymId { get; private set; }

    public Guid ExerciseId { get; private set; }

    public Guid AlternativeId { get; private set; }

    public static ExerciseAlternative Create(Guid? gymId, Guid first, Guid second)
    {
        if (first == Guid.Empty || second == Guid.Empty)
        {
            throw new ValidationDomainException("La alternativa necesita dos ejercicios.");
        }

        if (first == second)
        {
            throw new ValidationDomainException("Un ejercicio no puede ser alternativa de sí mismo.");
        }

        var (low, high) = Order(first, second);
        return new ExerciseAlternative
        {
            GymId = gymId == Guid.Empty ? null : gymId,
            ExerciseId = low,
            AlternativeId = high,
        };
    }

    /// <summary>El par en el orden en que se guarda (el mismo que usa Postgres para comparar <c>uuid</c>).</summary>
    public static (Guid Low, Guid High) Order(Guid a, Guid b)
        => string.CompareOrdinal(a.ToString("D"), b.ToString("D")) <= 0 ? (a, b) : (b, a);

    /// <summary>El otro extremo de la relación visto desde <paramref name="exerciseId"/>.</summary>
    public Guid Other(Guid exerciseId) => exerciseId == ExerciseId ? AlternativeId : ExerciseId;
}
