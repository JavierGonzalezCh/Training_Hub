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

        // Solo hace falta marcar cuáles están ocultos cuando la lista los incluye.
        var hidden = filter.IncludeHidden
            ? await exercises.GetHiddenIdsAsync(list.Select(e => e.Id).ToList(), cancellationToken)
            : new HashSet<Guid>();

        return list.Select(e => ExerciseMapper.ToResponse(e, hidden.Contains(e.Id))).ToList();
    }
}

public sealed class GetExerciseHandler(IExerciseRepository exercises)
{
    public async Task<ExerciseResponse> HandleAsync(Guid exerciseId, CancellationToken cancellationToken)
    {
        var exercise = await exercises.GetVisibleByIdAsync(exerciseId, cancellationToken)
            ?? throw new NotFoundException("Ejercicio no encontrado.");

        var hidden = await exercises.GetHiddenIdsAsync([exercise.Id], cancellationToken);
        return ExerciseMapper.ToResponse(exercise, hidden.Contains(exercise.Id));
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
            request.SecondaryMuscles, request.Equipment, request.VideoUrl, request.ImageUrl,
            request.ExerciseType ?? ExerciseType.Strength, request.TrackingType,
            request.IsUnilateral ?? false, request.Difficulty ?? Difficulty.Intermediate);

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
            request.SecondaryMuscles, request.Equipment, request.VideoUrl, request.ImageUrl,
            request.ExerciseType ?? ExerciseType.Strength, request.TrackingType,
            request.IsUnilateral ?? false, request.Difficulty ?? Difficulty.Intermediate);

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
/// Oculta un ejercicio del sistema para el gym del staff: deja de salir en el buscador y en el editor, y las
/// rutinas que ya lo usan no cambian. Solo aplica a ejercicios del sistema (uno del gym → 422). Idempotente.
/// </summary>
public sealed class HideExerciseHandler(
    ICurrentUserService currentUser, IExerciseRepository exercises, IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(Guid exerciseId, CancellationToken cancellationToken)
    {
        var gymId = ExerciseVisibility.RequireGym(currentUser);
        var exercise = await exercises.GetVisibleByIdAsync(exerciseId, cancellationToken)
            ?? throw new NotFoundException("Ejercicio no encontrado.");

        if (!exercise.IsSystem)
        {
            throw new ValidationDomainException(
                "Solo se pueden ocultar ejercicios del sistema; uno del gym se desactiva.");
        }

        if (await exercises.GetHiddenAsync(exerciseId, cancellationToken) is not null)
        {
            return;
        }

        exercises.AddHidden(GymHiddenExercise.Create(gymId, exerciseId));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Vuelve a mostrar un ejercicio del sistema que el gym había ocultado. Idempotente.</summary>
public sealed class ShowExerciseHandler(
    ICurrentUserService currentUser, IExerciseRepository exercises, IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(Guid exerciseId, CancellationToken cancellationToken)
    {
        ExerciseVisibility.RequireGym(currentUser);
        var exercise = await exercises.GetVisibleByIdAsync(exerciseId, cancellationToken)
            ?? throw new NotFoundException("Ejercicio no encontrado.");

        if (!exercise.IsSystem)
        {
            throw new ValidationDomainException("Solo los ejercicios del sistema se ocultan o se muestran.");
        }

        if (await exercises.GetHiddenAsync(exerciseId, cancellationToken) is not { } hidden)
        {
            return;
        }

        exercises.RemoveHidden(hidden);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Crea una copia del ejercicio para el gym del staff, con todos los campos y los mismos archivos de medios.
/// El nombre es "{original} (copia)" (con número si ya existe). <c>HideOriginal</c> oculta el original en el
/// mismo paso para que el gym quede solo con su versión (solo si el original es del sistema).
/// </summary>
public sealed class DuplicateExerciseHandler(
    ICurrentUserService currentUser, IExerciseRepository exercises, IUnitOfWork unitOfWork)
{
    public async Task<ExerciseResponse> HandleAsync(
        Guid exerciseId, DuplicateExerciseRequest request, CancellationToken cancellationToken)
    {
        var gymId = ExerciseVisibility.RequireGym(currentUser);
        var original = await exercises.GetVisibleByIdAsync(exerciseId, cancellationToken)
            ?? throw new NotFoundException("Ejercicio no encontrado.");

        var name = $"{original.Name} (copia)";
        for (var n = 2; await exercises.NameTakenAsync(name, gymId, null, cancellationToken); n++)
        {
            name = $"{original.Name} (copia {n})";
        }

        var copy = original.DuplicateFor(gymId, name);
        exercises.Add(copy);

        if (request.HideOriginal && original.IsSystem
            && await exercises.GetHiddenAsync(original.Id, cancellationToken) is null)
        {
            exercises.AddHidden(GymHiddenExercise.Create(gymId, original.Id));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ExerciseMapper.ToResponse(copy);
    }
}

internal static class ExerciseVisibility
{
    /// <summary>Ocultar y duplicar son del staff de un gym; un superadmin no tiene gym desde el que hacerlo.</summary>
    public static Guid RequireGym(ICurrentUserService currentUser)
    {
        if (currentUser.IsSuperAdmin)
        {
            throw new ForbiddenDomainException("Un superadmin no oculta ni duplica ejercicios: no tiene gym.");
        }

        return currentUser.RequireGymId();
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

