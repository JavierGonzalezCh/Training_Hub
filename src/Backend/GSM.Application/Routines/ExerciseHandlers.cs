using GSM.Application.Common.Abstractions;
using GSM.Application.Routines.Abstractions;
using GSM.Application.Routines.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Routines;

namespace GSM.Application.Routines;

public sealed class ListExercisesHandler(IExerciseRepository exercises)
{
    public async Task<IReadOnlyList<ExerciseResponse>> HandleAsync(
        ExerciseFilter filter, CancellationToken cancellationToken)
    {
        var list = await exercises.ListVisibleAsync(filter, cancellationToken);
        return list.Select(ExerciseMapper.ToResponse).ToList();
    }
}

public sealed class GetExerciseHandler(IExerciseRepository exercises)
{
    public async Task<ExerciseResponse> HandleAsync(Guid exerciseId, CancellationToken cancellationToken)
    {
        var exercise = await exercises.GetVisibleByIdAsync(exerciseId, cancellationToken)
            ?? throw new NotFoundException("Ejercicio no encontrado.");

        return ExerciseMapper.ToResponse(exercise);
    }
}

public sealed class CreateExerciseHandler(
    ICurrentUserService currentUser, IExerciseRepository exercises, IUnitOfWork unitOfWork)
{
    public async Task<ExerciseResponse> HandleAsync(
        CreateExerciseRequest request, CancellationToken cancellationToken)
    {
        // Un superadmin (sin gym) crea en el catálogo del sistema; un staff, en el de su gym.
        var gymId = currentUser.IsSuperAdmin ? (Guid?)null : currentUser.RequireGymId();
        var name = request.Name.Trim();

        if (await exercises.NameTakenAsync(name, gymId, null, cancellationToken))
        {
            throw new ConflictException($"Ya existe un ejercicio con el nombre '{name}'.");
        }

        var exercise = Exercise.Create(
            gymId, name, request.Description, request.PrimaryMuscle,
            request.SecondaryMuscles, request.Equipment, request.VideoUrl, request.ImageUrl);

        exercises.Add(exercise);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ExerciseMapper.ToResponse(exercise);
    }
}

public sealed class UpdateExerciseHandler(
    ICurrentUserService currentUser, IExerciseRepository exercises, IUnitOfWork unitOfWork)
{
    public async Task<ExerciseResponse> HandleAsync(
        Guid exerciseId, UpdateExerciseRequest request, CancellationToken cancellationToken)
    {
        var exercise = await exercises.GetVisibleByIdAsync(exerciseId, cancellationToken)
            ?? throw new NotFoundException("Ejercicio no encontrado.");

        ExerciseAccess.EnsureCanManage(currentUser, exercise);

        var name = request.Name.Trim();
        if (await exercises.NameTakenAsync(name, exercise.GymId, exercise.Id, cancellationToken))
        {
            throw new ConflictException($"Ya existe otro ejercicio con el nombre '{name}'.");
        }

        exercise.Update(
            name, request.Description, request.PrimaryMuscle,
            request.SecondaryMuscles, request.Equipment, request.VideoUrl, request.ImageUrl);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ExerciseMapper.ToResponse(exercise);
    }
}

public sealed class DeactivateExerciseHandler(
    ICurrentUserService currentUser, IExerciseRepository exercises, IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(Guid exerciseId, CancellationToken cancellationToken)
    {
        var exercise = await exercises.GetVisibleByIdAsync(exerciseId, cancellationToken)
            ?? throw new NotFoundException("Ejercicio no encontrado.");

        ExerciseAccess.EnsureCanManage(currentUser, exercise);

        if (!exercise.IsActive)
        {
            return;
        }

        exercise.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// La autorización "global vs del gym" se resuelve aquí, no solo con la policy del controller:
/// un <c>staff</c> que toque una fila del sistema recibe <c>403</c>.
/// </summary>
internal static class ExerciseAccess
{
    public static void EnsureCanManage(ICurrentUserService currentUser, Exercise exercise)
    {
        if (exercise.IsSystem)
        {
            if (!currentUser.IsSuperAdmin)
            {
                throw new ForbiddenDomainException(
                    "Solo un superadmin administra el catálogo del sistema.");
            }

            return;
        }

        if (currentUser.IsSuperAdmin)
        {
            throw new ForbiddenDomainException("Un superadmin no administra ejercicios de un gym.");
        }

        if (exercise.GymId != currentUser.RequireGymId())
        {
            throw new NotFoundException("Ejercicio no encontrado.");
        }
    }
}
