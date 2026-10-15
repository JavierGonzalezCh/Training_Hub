using GSM.Domain.Routines;

namespace GSM.Application.Routines.Abstractions;

/// <summary>
/// Alternativas recomendadas. Como el catálogo, no pasan por el Global Query Filter: toda lectura aplica
/// <c>gym_id IS NULL OR gym_id = @current</c>. Sin <c>SaveChanges</c>.
/// </summary>
public interface IExerciseAlternativeRepository
{
    /// <summary>Los pares en que participa el ejercicio (en cualquiera de los dos extremos) que ve el gym actual.</summary>
    Task<IReadOnlyList<ExerciseAlternative>> ListForAsync(
        Guid exerciseId, CancellationToken cancellationToken = default);

    /// <summary>El par definido en ese ámbito (<c>null</c> = sistema), en cualquier orden.</summary>
    Task<ExerciseAlternative?> GetAsync(
        Guid? gymId, Guid first, Guid second, CancellationToken cancellationToken = default);

    void Add(ExerciseAlternative alternative);

    void Remove(ExerciseAlternative alternative);
}
