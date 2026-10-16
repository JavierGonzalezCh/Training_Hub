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

/// <summary>
/// Imagen de un ejercicio del sistema (JPEG, PNG o WEBP, hasta 5 MB). Reemplaza <c>ImageUrl</c> y borra el archivo
/// anterior si era una subida nuestra. Solo SuperAdmin.
/// </summary>
public sealed class UploadExerciseImageHandler(
    ICurrentUserService currentUser,
    IExerciseRepository exercises,
    ExerciseEquipmentResolver equipment,
    IFileStorage storage,
    IUnitOfWork unitOfWork)
{
    public async Task<ExerciseResponse> HandleAsync(
        Guid exerciseId, FileToStore file, CancellationToken cancellationToken)
    {
        ImageUploadRules.EnsureValid(file);

        var exercise = await exercises.GetVisibleByIdAsync(exerciseId, cancellationToken)
            ?? throw new NotFoundException("Ejercicio no encontrado.");

        ExerciseAccess.EnsureCanManage(currentUser, exercise);

        var previous = exercise.ImageUrl;
        var url = await storage.SaveAsync(file, "exercises", cancellationToken);

        exercise.Update(
            exercise.Name, exercise.Description, exercise.PrimaryMuscle, exercise.SecondaryMuscles,
            exercise.Equipment, exercise.VideoUrl, url, exercise.ExerciseType, exercise.TrackingType,
            exercise.IsUnilateral, exercise.Difficulty);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(previous))
        {
            await storage.DeleteAsync(previous, cancellationToken);
        }

        return ExerciseMapper.ToResponse(
            exercise, false, await equipment.ResolveOneAsync(exercise, cancellationToken));
    }
}
