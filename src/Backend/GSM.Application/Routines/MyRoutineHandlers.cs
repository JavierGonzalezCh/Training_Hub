using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms.Public;
using GSM.Application.Members.Public;
using GSM.Application.Routines.Abstractions;
using GSM.Application.Routines.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Routines;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Routines;

/// <summary>Resuelve la rutina que el miembro puede tocar: la suya, vigente y armada por él mismo.</summary>
internal static class MyRoutineAccess
{
    public static async Task<Routine> RequireOwnActiveAsync(
        ICurrentUserService currentUser, IMemberReader members, IRoutineRepository routines, Guid routineId,
        CancellationToken cancellationToken)
    {
        var self = await members.ResolveByUserIdAsync(currentUser.UserId ?? Guid.Empty, cancellationToken)
            ?? throw new ForbiddenDomainException("Tu cuenta no está vinculada a un miembro.");

        var routine = await routines.GetWithDaysAsync(routineId, cancellationToken);
        if (routine is null || routine.MemberId != self.Id)
        {
            throw new NotFoundException("Rutina no encontrada.");
        }

        if (!routine.SelfAssigned)
        {
            throw new ForbiddenDomainException("Esta rutina la armó tu entrenador: pídele los cambios a él.");
        }

        if (!routine.IsActive)
        {
            throw new ConflictException("Esta rutina ya está dada de baja.");
        }

        return routine;
    }
}

/// <summary>
/// El miembro edita en el lugar una rutina que armó él mismo. Misma regla de la edición del staff (lo que ya no viene se
/// retira con fecha, sin borrarse), y la rutina queda pendiente de revisión del entrenador. 403 con el auto-servicio apagado.
/// </summary>
public sealed class UpdateMyRoutineHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    IMemberReader members,
    IExerciseRepository exercises,
    ExerciseEquipmentResolver equipment,
    IRoutineRepository routines,
    IUnitOfWork unitOfWork,
    ILogger<UpdateMyRoutineHandler> logger)
{
    public async Task<RoutineResponse> HandleAsync(
        Guid routineId, SaveRoutineRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        await RoutineFeatureGate.EnsureMemberModuleEnabledAsync(gyms, gymId, cancellationToken);
        await RoutineFeatureGate.EnsureSelfServiceEnabledAsync(gyms, gymId, cancellationToken);

        var routine = await MyRoutineAccess.RequireOwnActiveAsync(currentUser, members, routines, routineId, cancellationToken);

        var catalog = await RoutineDrafting.ValidateExercisesVisibleAsync(exercises, request, cancellationToken);
        var draft = RoutineDrafting.ToDraft(
            request, catalog, routine.AssignedBy, selfAssigned: true, keepIds: true, mode: routine.ScheduleMode);

        routine.Replace(draft, clock.Today, editedByMember: true);
        routines.TrackNewChildren(routine);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Miembro editó su rutina {RoutineId} ({Days} días)", routine.Id, routine.CycleLength);
        return await RoutineDrafting.BuildResponseAsync(exercises, equipment, routine, cancellationToken);
    }
}

/// <summary>
/// El miembro elimina su propia rutina: se da de baja (conserva días, ejercicios y registros: alimentan el historial y la
/// racha) y queda sin rutina activa. Solo las que armó él mismo.
/// </summary>
public sealed class DeactivateMyRoutineHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    IMemberReader members,
    IRoutineRepository routines,
    IUnitOfWork unitOfWork,
    ILogger<DeactivateMyRoutineHandler> logger)
{
    public async Task HandleAsync(Guid routineId, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        await RoutineFeatureGate.EnsureMemberModuleEnabledAsync(gyms, gymId, cancellationToken);

        var routine = await MyRoutineAccess.RequireOwnActiveAsync(currentUser, members, routines, routineId, cancellationToken);

        routine.Deactivate(clock.Today);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Miembro dio de baja su rutina {RoutineId}", routine.Id);
    }
}
