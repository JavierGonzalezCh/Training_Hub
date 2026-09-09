using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Gyms.Public;
using GSM.Application.Members.Public;
using GSM.Application.Routines.Abstractions;
using GSM.Application.Routines.Contracts;
using GSM.Application.Routines.Events;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Routines;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Routines;

/// <summary>Utilidades compartidas por los casos de uso de rutinas.</summary>
internal static class RoutineDrafting
{
    public static RoutineDraft ToDraft(SaveRoutineRequest request, Guid assignedBy, bool selfAssigned)
        => new(
            request.Name,
            request.Notes,
            assignedBy,
            selfAssigned,
            (request.Days ?? []).Select(d => new RoutineDayDraft(
                d.Name,
                d.IsRest,
                (d.Exercises ?? []).Select(e => new RoutineExerciseDraft(
                    e.ExerciseId, e.TargetSets, e.TargetReps, e.TargetWeightKg, e.RestSeconds, e.Notes)).ToList())).ToList());

    public static async Task ValidateExercisesVisibleAsync(
        IExerciseRepository exercises, SaveRoutineRequest request, CancellationToken cancellationToken)
    {
        var ids = (request.Days ?? [])
            .SelectMany(d => d.Exercises ?? [])
            .Select(e => e.ExerciseId)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            return;
        }

        var visible = await exercises.FilterVisibleIdsAsync(ids, cancellationToken);
        var missing = ids.Where(id => !visible.Contains(id)).ToList();
        if (missing.Count > 0)
        {
            throw new ValidationDomainException(
                "La rutina referencia ejercicios que no existen o son de otro gym.");
        }
    }

    public static async Task<RoutineResponse> BuildResponseAsync(
        IExerciseRepository exercises, Routine routine, CancellationToken cancellationToken)
    {
        var names = await exercises.GetVisibleNamesAsync(RoutineMapper.ExerciseIds(routine), cancellationToken);
        return RoutineMapper.ToResponse(routine, names);
    }
}

public sealed class AssignRoutineHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberReader members,
    IExerciseRepository exercises,
    IRoutineRepository routines,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<AssignRoutineHandler> logger)
{
    public async Task<RoutineResponse> HandleAsync(
        Guid memberId, SaveRoutineRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();

        if (!await members.ExistsAsync(memberId, cancellationToken))
        {
            throw new NotFoundException("Miembro no encontrado.");
        }

        await RoutineDrafting.ValidateExercisesVisibleAsync(exercises, request, cancellationToken);

        var draft = RoutineDrafting.ToDraft(request, currentUser.UserId ?? Guid.Empty, selfAssigned: false);
        var routine = Routine.Assign(gymId, memberId, clock.Today, draft);

        await unitOfWork.ExecuteInTransactionAsync(async innerToken =>
        {
            foreach (var active in await routines.GetActiveForMemberAsync(memberId, innerToken))
            {
                active.Deactivate(clock.Today);
            }

            routines.Add(routine);
            await unitOfWork.SaveChangesAsync(innerToken);
        }, cancellationToken);

        logger.LogInformation(
            "Rutina {RoutineId} asignada al socio {MemberId} ({Days} días)",
            routine.Id, memberId, routine.CycleLength);

        await events.PublishAsync(
            new RoutineAssigned(routine.Id, memberId, gymId, SelfAssigned: false), cancellationToken);

        return await RoutineDrafting.BuildResponseAsync(exercises, routine, cancellationToken);
    }
}

public sealed class UpdateRoutineHandler(
    ICurrentUserService currentUser,
    IExerciseRepository exercises,
    IRoutineRepository routines,
    IUnitOfWork unitOfWork,
    ILogger<UpdateRoutineHandler> logger)
{
    public async Task<RoutineResponse> HandleAsync(
        Guid routineId, SaveRoutineRequest request, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var routine = await routines.GetWithDaysAsync(routineId, cancellationToken)
            ?? throw new NotFoundException("Rutina no encontrada.");

        await RoutineDrafting.ValidateExercisesVisibleAsync(exercises, request, cancellationToken);

        var draft = RoutineDrafting.ToDraft(request, routine.AssignedBy, routine.SelfAssigned);

        await unitOfWork.ExecuteInTransactionAsync(async innerToken =>
        {
            routines.RemoveDaysOf(routine);
            await unitOfWork.SaveChangesAsync(innerToken);

            routine.Replace(draft);
            await unitOfWork.SaveChangesAsync(innerToken);
        }, cancellationToken);

        logger.LogInformation("Rutina {RoutineId} reemplazada ({Days} días)", routine.Id, routine.CycleLength);
        return await RoutineDrafting.BuildResponseAsync(exercises, routine, cancellationToken);
    }
}

public sealed class DeactivateRoutineHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IRoutineRepository routines,
    IUnitOfWork unitOfWork,
    ILogger<DeactivateRoutineHandler> logger)
{
    public async Task HandleAsync(Guid routineId, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var routine = await routines.GetWithDaysAsync(routineId, cancellationToken)
            ?? throw new NotFoundException("Rutina no encontrada.");

        if (!routine.IsActive)
        {
            return;
        }

        routine.Deactivate(clock.Today);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Rutina {RoutineId} dada de baja el {Date}", routine.Id, routine.DeactivatedOn);
    }
}

public sealed class GetRoutineHandler(
    ICurrentUserService currentUser, IExerciseRepository exercises, IRoutineRepository routines)
{
    public async Task<RoutineResponse> HandleAsync(Guid routineId, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var routine = await routines.GetWithDaysAsync(routineId, cancellationToken)
            ?? throw new NotFoundException("Rutina no encontrada.");

        return await RoutineDrafting.BuildResponseAsync(exercises, routine, cancellationToken);
    }
}

public sealed class ListMemberRoutinesHandler(
    ICurrentUserService currentUser, IMemberReader members, IRoutineRepository routines)
{
    public async Task<IReadOnlyList<RoutineListItem>> HandleAsync(
        Guid memberId, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        if (!await members.ExistsAsync(memberId, cancellationToken))
        {
            throw new NotFoundException("Miembro no encontrado.");
        }

        var list = await routines.ListForMemberAsync(memberId, cancellationToken);
        return list.Select(RoutineMapper.ToListItem).ToList();
    }
}

/// <summary>Rutina activa del socio + qué toca hoy, resolviendo la posición del ciclo.</summary>
public sealed class GetMyRoutineHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    IMemberReader members,
    IExerciseRepository exercises,
    IRoutineRepository routines)
{
    public async Task<MyRoutineResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        await RoutineFeatureGate.EnsureMemberModuleEnabledAsync(gyms, gymId, cancellationToken);

        var self = await members.ResolveByUserIdAsync(currentUser.UserId ?? Guid.Empty, cancellationToken)
            ?? throw new ForbiddenDomainException("Tu cuenta no está vinculada a un miembro.");

        var routine = await routines.GetActiveForMemberWithDaysAsync(self.Id, cancellationToken);
        if (routine is null)
        {
            return new MyRoutineResponse(null, null);
        }

        var names = await exercises.GetVisibleNamesAsync(
            RoutineMapper.ExerciseIds(routine), cancellationToken);
        var response = RoutineMapper.ToResponse(routine, names);

        var today = clock.Today;
        var position = RoutineCycle.PositionOn(routine.StartsOn, routine.CycleLength, today);
        var day = routine.Days.OrderBy(d => d.Position).First(d => d.Position == position);

        var todayView = new TodayView(
            today,
            position,
            day.Name ?? $"Día {day.Position}",
            day.IsRest,
            day.Exercises.OrderBy(e => e.Order)
                .Select(e => RoutineMapper.ToExerciseResponse(e, names)).ToList());

        return new MyRoutineResponse(response, todayView);
    }
}
