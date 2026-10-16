using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Routines.Abstractions;
using GSM.Application.Routines.Contracts;
using GSM.Domain.Common.Exceptions;

namespace GSM.Application.Routines;

/// <summary>
/// Imagen de un ejercicio (JPEG, PNG o WEBP, hasta 5 MB). Reemplaza <c>ImageUrl</c> y borra el archivo anterior si era
/// una subida nuestra y nadie más lo usa. El SuperAdmin sube las de los ejercicios del sistema; el staff, las de los
/// ejercicios de su gym (R6).
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

        await ExerciseMediaCleanup.DeletePreviousAsync(exercises, storage, exercise.Id, previous, cancellationToken);

        return ExerciseMapper.ToResponse(
            exercise, false, await equipment.ResolveOneAsync(exercise, cancellationToken));
    }
}

/// <summary>
/// Video corto de un ejercicio (MP4, hasta 50 MB). Reemplaza <c>VideoUrl</c> —que también puede ser un enlace externo
/// de YouTube o Vimeo— y borra el archivo anterior solo si era una subida nuestra y nadie más lo usa.
/// </summary>
public sealed class UploadExerciseVideoHandler(
    ICurrentUserService currentUser,
    IExerciseRepository exercises,
    ExerciseEquipmentResolver equipment,
    IFileStorage storage,
    IUnitOfWork unitOfWork)
{
    public async Task<ExerciseResponse> HandleAsync(
        Guid exerciseId, FileToStore file, CancellationToken cancellationToken)
    {
        VideoUploadRules.EnsureValid(file);

        var exercise = await exercises.GetVisibleByIdAsync(exerciseId, cancellationToken)
            ?? throw new NotFoundException("Ejercicio no encontrado.");

        ExerciseAccess.EnsureCanManage(currentUser, exercise);

        var previous = exercise.VideoUrl;
        var url = await storage.SaveAsync(file, "exercise-videos", cancellationToken);

        exercise.Update(
            exercise.Name, exercise.Description, exercise.PrimaryMuscle, exercise.SecondaryMuscles,
            exercise.Equipment, url, exercise.ImageUrl, exercise.ExerciseType, exercise.TrackingType,
            exercise.IsUnilateral, exercise.Difficulty);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await ExerciseMediaCleanup.DeletePreviousAsync(exercises, storage, exercise.Id, previous, cancellationToken);

        return ExerciseMapper.ToResponse(
            exercise, false, await equipment.ResolveOneAsync(exercise, cancellationToken));
    }
}

internal static class ExerciseMediaCleanup
{
    /// <summary>
    /// Borra el archivo anterior salvo que otro ejercicio lo comparta (un duplicado conserva las mismas URLs).
    /// Un enlace externo (YouTube) no es de R2: <c>DeleteAsync</c> lo ignora.
    /// </summary>
    public static async Task DeletePreviousAsync(
        IExerciseRepository exercises, IFileStorage storage, Guid exerciseId, string? previous,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(previous))
        {
            return;
        }

        if (await exercises.IsMediaUrlInUseAsync(previous, exerciseId, cancellationToken))
        {
            return;
        }

        await storage.DeleteAsync(previous, cancellationToken);
    }
}
