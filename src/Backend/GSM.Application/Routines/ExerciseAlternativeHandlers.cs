using GSM.Application.Common.Abstractions;
using GSM.Application.Routines.Abstractions;
using GSM.Application.Routines.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Routines;

namespace GSM.Application.Routines;

/// <summary>
/// Alternativas de un ejercicio, recomendadas primero y luego sugeridas (máximo 6). Las puede pedir cualquier
/// sesión autenticada: el staff para "Cambiar por…" y el miembro para "Hacer otro ejercicio hoy".
/// </summary>
public sealed class GetExerciseAlternativesHandler(
    IExerciseRepository exercises, ExerciseAlternativesResolver resolver)
{
    public async Task<IReadOnlyList<ExerciseAlternativeResponse>> HandleAsync(
        Guid exerciseId, CancellationToken cancellationToken)
    {
        var exercise = await exercises.GetVisibleByIdAsync(exerciseId, cancellationToken)
            ?? throw new NotFoundException("Ejercicio no encontrado.");

        var list = await resolver.ResolveAsync(exercise, cancellationToken);
        return list.Select(ExerciseAlternativeMapper.ToResponse).ToList();
    }
}

/// <summary>
/// Guarda una alternativa recomendada entre dos ejercicios, en los dos sentidos. Un superadmin la guarda para el
/// sistema (entre ejercicios del sistema); el staff, para su gym (entre cualquier ejercicio que vea). Idempotente.
/// </summary>
public sealed class AddExerciseAlternativeHandler(
    ICurrentUserService currentUser,
    IExerciseRepository exercises,
    IExerciseAlternativeRepository alternatives,
    IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(Guid exerciseId, Guid alternativeId, CancellationToken cancellationToken)
    {
        var scope = ExerciseAlternativeScope.Of(currentUser);

        // Un superadmin solo ve el catálogo del sistema, así que un ejercicio de un gym sale como no encontrado.
        _ = await exercises.GetVisibleByIdAsync(exerciseId, cancellationToken)
            ?? throw new NotFoundException("Ejercicio no encontrado.");
        _ = await exercises.GetVisibleByIdAsync(alternativeId, cancellationToken)
            ?? throw new NotFoundException("Ejercicio alternativo no encontrado.");

        var pair = ExerciseAlternative.Create(scope, exerciseId, alternativeId);

        if (await alternatives.GetAsync(scope, exerciseId, alternativeId, cancellationToken) is not null
            || (scope is not null && await alternatives.GetAsync(null, exerciseId, alternativeId, cancellationToken) is not null))
        {
            return;
        }

        alternatives.Add(pair);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Quita una alternativa recomendada del ámbito de quien llama (sistema para el superadmin, gym para el staff).
/// Idempotente. Un gym no quita una alternativa del sistema (422).
/// </summary>
public sealed class RemoveExerciseAlternativeHandler(
    ICurrentUserService currentUser,
    IExerciseAlternativeRepository alternatives,
    IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(Guid exerciseId, Guid alternativeId, CancellationToken cancellationToken)
    {
        var scope = ExerciseAlternativeScope.Of(currentUser);

        if (await alternatives.GetAsync(scope, exerciseId, alternativeId, cancellationToken) is { } pair)
        {
            alternatives.Remove(pair);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        if (scope is not null && await alternatives.GetAsync(null, exerciseId, alternativeId, cancellationToken) is not null)
        {
            throw new ValidationDomainException(
                "Esa alternativa es del sistema: un gym no la quita.");
        }
    }
}

internal static class ExerciseAlternativeScope
{
    /// <summary>El ámbito donde escribe quien llama: <c>null</c> = sistema (superadmin), el gym del staff en otro caso.</summary>
    public static Guid? Of(ICurrentUserService currentUser)
        => currentUser.IsSuperAdmin ? null : currentUser.RequireGymId();
}

public static class ExerciseAlternativeMapper
{
    public static ExerciseAlternativeResponse ToResponse(ExerciseAlternativeCandidate candidate)
    {
        var e = candidate.Exercise;
        return new ExerciseAlternativeResponse(
            e.Id,
            e.Name,
            candidate.Source,
            e.PrimaryMuscle.ToString(),
            e.MuscleGroup.ToString(),
            e.ExerciseType.ToString(),
            e.TrackingType.ToString(),
            e.IsUnilateral,
            e.Difficulty.ToString(),
            e.ImageUrl,
            candidate.Equipment?.IsAvailable ?? true,
            candidate.Equipment?.Machines ?? []);
    }
}
