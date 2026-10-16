using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Routines.Abstractions;
using GSM.Application.Routines.Contracts;
using GSM.Domain.Common.Exceptions;

namespace GSM.Application.Routines;

/// <summary>
/// S4: lo que le faltaba al catálogo de ejercicios del sistema para administrarse desde <c>/admin</c>. Crear, editar,
/// desactivar, alternativas y ligas con modelos ya existen (R3, R5, E3) y las reutiliza el controller de catálogos.
/// </summary>
public sealed class ActivateExerciseHandler(
    ICurrentUserService currentUser,
    IExerciseRepository exercises,
    ExerciseEquipmentResolver equipment,
    IUnitOfWork unitOfWork)
{
    public async Task<ExerciseResponse> HandleAsync(Guid exerciseId, CancellationToken cancellationToken)
    {
        var exercise = await exercises.GetVisibleByIdAsync(exerciseId, cancellationToken)
            ?? throw new NotFoundException("Ejercicio no encontrado.");

        ExerciseAccess.EnsureCanManage(currentUser, exercise);

        if (!exercise.IsActive)
        {
            exercise.Activate();
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return ExerciseMapper.ToResponse(
            exercise, false, await equipment.ResolveOneAsync(exercise, cancellationToken));
    }
}
