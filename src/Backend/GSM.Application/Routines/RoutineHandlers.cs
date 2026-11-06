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
    public static RoutineDraft ToDraft(
        SaveRoutineRequest request, IReadOnlyDictionary<Guid, ExerciseSummary> catalog, Guid assignedBy,
        bool selfAssigned, bool keepIds = false, ScheduleMode? mode = null)
    {
        var draft = BuildDraft(
            request, catalog, assignedBy, selfAssigned, mode ?? RoutineSchedule.ParseMode(request.ScheduleMode));
        return keepIds ? draft : StripIds(draft);
    }

    // Al crear, un id en el body no significa nada (no hay nada que actualizar): se descarta.
    private static RoutineDraft StripIds(RoutineDraft draft)
        => draft with
        {
            Days = draft.Days.Select(d => d with
            {
                Id = null,
                Exercises = d.Exercises.Select(e => e with { Id = null }).ToList(),
            }).ToList(),
        };

    private static RoutineDraft BuildDraft(
        SaveRoutineRequest request, IReadOnlyDictionary<Guid, ExerciseSummary> catalog, Guid assignedBy,
        bool selfAssigned, ScheduleMode mode)
        => new(
            request.Name,
            request.Notes,
            assignedBy,
            selfAssigned,
            (request.Days ?? []).Select(d => new RoutineDayDraft(
                d.Name,
                d.IsRest,
                (d.Exercises ?? []).Select(e => new RoutineExerciseDraft(
                    e.ExerciseId, ToSetTargets(e, catalog), e.RestSeconds, e.Notes, e.Group, e.Id)).ToList(),
                d.Id,
                d.DayOfWeek)).ToList(),
            mode);

    // Las series se normalizan contra el tracking_type del ejercicio: solo quedan los campos que corresponden.
    private static List<SetTarget> ToSetTargets(
        RoutineExerciseInput line, IReadOnlyDictionary<Guid, ExerciseSummary> catalog)
    {
        var tracking = catalog.TryGetValue(line.ExerciseId, out var info) ? info.TrackingType : TrackingType.RepsWeight;
        var targets = (line.SetTargets ?? []).Select(t => new SetTarget(
            0,
            SetTypes.Parse(t.SetType) ?? throw new ValidationDomainException("Tipo de serie inválido."),
            t.RepsMin, t.RepsMax, t.WeightKg, t.DurationSeconds, t.DistanceM, t.Rir)).ToList();
        return SetTargetRules.Normalize(tracking, targets);
    }

    /// <summary>Valida que cada ejercicio exista y sea visible para el gym; devuelve su resumen (con el tracking).</summary>
    public static async Task<IReadOnlyDictionary<Guid, ExerciseSummary>> ValidateExercisesVisibleAsync(
        IExerciseRepository exercises, SaveRoutineRequest request, CancellationToken cancellationToken)
    {
        var ids = (request.Days ?? [])
            .SelectMany(d => d.Exercises ?? [])
            .Select(e => e.ExerciseId)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            return new Dictionary<Guid, ExerciseSummary>();
        }

        var visible = await exercises.GetVisibleSummariesAsync(ids, cancellationToken);
        var missing = ids.Where(id => !visible.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            throw new ValidationDomainException(
                "La rutina referencia ejercicios que no existen o son de otro gym.");
        }

        return visible;
    }

    public static async Task<RoutineResponse> BuildResponseAsync(
        IExerciseRepository exercises,
        ExerciseEquipmentResolver equipment,
        Routine routine,
        CancellationToken cancellationToken)
    {
        var names = await exercises.GetVisibleSummariesAsync(RoutineMapper.ExerciseIds(routine), cancellationToken);
        var machines = await equipment.ResolveAsync(RoutineMapper.EquipmentNeeds(routine, names), cancellationToken);
        return RoutineMapper.ToResponse(routine, names, machines);
    }
}

public sealed class AssignRoutineHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberReader members,
    IExerciseRepository exercises,
    ExerciseEquipmentResolver equipment,
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

        var catalog = await RoutineDrafting.ValidateExercisesVisibleAsync(exercises, request, cancellationToken);

        var draft = RoutineDrafting.ToDraft(request, catalog, currentUser.UserId ?? Guid.Empty, selfAssigned: false);
        var reviewWeeks = await RoutineSettingsLookup.GetReviewWeeksAsync(routines, gymId, cancellationToken);
        var routine = Routine.Assign(gymId, memberId, clock.Today, draft, reviewWeeks);

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
            "Rutina {RoutineId} asignada al miembro {MemberId} ({Days} días)",
            routine.Id, memberId, routine.CycleLength);

        await events.PublishAsync(
            new RoutineAssigned(routine.Id, memberId, gymId, SelfAssigned: false), cancellationToken);

        return await RoutineDrafting.BuildResponseAsync(exercises, equipment, routine, cancellationToken);
    }
}

/// <summary>Auto-servicio: el miembro se arma su propia rutina (<c>self_assigned = true</c>). 403 si el flag está apagado.</summary>
public sealed class SelfAssignRoutineHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    IMemberReader members,
    IExerciseRepository exercises,
    ExerciseEquipmentResolver equipment,
    IRoutineRepository routines,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<SelfAssignRoutineHandler> logger)
{
    public async Task<RoutineResponse> HandleAsync(SaveRoutineRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        await RoutineFeatureGate.EnsureMemberModuleEnabledAsync(gyms, gymId, cancellationToken);
        await RoutineFeatureGate.EnsureSelfServiceEnabledAsync(gyms, gymId, cancellationToken);

        var self = await members.ResolveByUserIdAsync(currentUser.UserId ?? Guid.Empty, cancellationToken)
            ?? throw new ForbiddenDomainException("Tu cuenta no está vinculada a un miembro.");

        var catalog = await RoutineDrafting.ValidateExercisesVisibleAsync(exercises, request, cancellationToken);

        var draft = RoutineDrafting.ToDraft(request, catalog, currentUser.UserId ?? Guid.Empty, selfAssigned: true);
        var reviewWeeks = await RoutineSettingsLookup.GetReviewWeeksAsync(routines, gymId, cancellationToken);
        var routine = Routine.Assign(gymId, self.Id, clock.Today, draft, reviewWeeks);

        await unitOfWork.ExecuteInTransactionAsync(async innerToken =>
        {
            foreach (var active in await routines.GetActiveForMemberAsync(self.Id, innerToken))
            {
                active.Deactivate(clock.Today);
            }

            routines.Add(routine);
            await unitOfWork.SaveChangesAsync(innerToken);
        }, cancellationToken);

        logger.LogInformation("Miembro {MemberId} se auto-asignó la rutina {RoutineId}", self.Id, routine.Id);
        await events.PublishAsync(
            new RoutineAssigned(routine.Id, self.Id, gymId, SelfAssigned: true), cancellationToken);

        return await RoutineDrafting.BuildResponseAsync(exercises, equipment, routine, cancellationToken);
    }
}

/// <summary>
/// Edita una rutina en el lugar. Si el body trae un modo de calendario distinto del de la rutina no es una
/// edición: se crea una rutina nueva con ese modo, la anterior se desactiva (conserva sus registros) y la
/// respuesta trae el <c>Id</c> de la nueva. Cambiar el modo sobre la misma rutina rompería la racha calculada
/// hacia atrás.
/// </summary>
public sealed class UpdateRoutineHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IExerciseRepository exercises,
    ExerciseEquipmentResolver equipment,
    IRoutineRepository routines,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<UpdateRoutineHandler> logger)
{
    public async Task<RoutineResponse> HandleAsync(
        Guid routineId, SaveRoutineRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();

        var routine = await routines.GetWithDaysAsync(routineId, cancellationToken)
            ?? throw new NotFoundException("Rutina no encontrada.");

        var requestedMode = request.ScheduleMode is null
            ? routine.ScheduleMode
            : RoutineSchedule.ParseMode(request.ScheduleMode);

        var catalog = await RoutineDrafting.ValidateExercisesVisibleAsync(exercises, request, cancellationToken);

        if (requestedMode != routine.ScheduleMode)
        {
            return await ChangeModeAsync(gymId, routine, request, catalog, requestedMode, cancellationToken);
        }

        var draft = RoutineDrafting.ToDraft(
            request, catalog, routine.AssignedBy, routine.SelfAssigned, keepIds: true, mode: routine.ScheduleMode);

        routine.Replace(draft, clock.Today);
        routines.TrackNewChildren(routine);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Rutina {RoutineId} editada en el lugar ({Days} días)", routine.Id, routine.CycleLength);
        return await RoutineDrafting.BuildResponseAsync(exercises, equipment, routine, cancellationToken);
    }

    private async Task<RoutineResponse> ChangeModeAsync(
        Guid gymId, Routine previous, SaveRoutineRequest request, IReadOnlyDictionary<Guid, ExerciseSummary> catalog,
        ScheduleMode mode, CancellationToken cancellationToken)
    {
        if (!previous.IsActive)
        {
            throw new ConflictException(
                "La rutina está dada de baja: para cambiar el modo de calendario crea una rutina nueva.");
        }

        // Los ids del body son de la rutina anterior y no significan nada en la nueva: se descartan.
        var draft = RoutineDrafting.ToDraft(request, catalog, previous.AssignedBy, previous.SelfAssigned, mode: mode);
        var reviewWeeks = await RoutineSettingsLookup.GetReviewWeeksAsync(routines, gymId, cancellationToken);
        var replacement = Routine.Assign(gymId, previous.MemberId, clock.Today, draft, reviewWeeks);

        await unitOfWork.ExecuteInTransactionAsync(async innerToken =>
        {
            foreach (var active in await routines.GetActiveForMemberAsync(previous.MemberId, innerToken))
            {
                active.Deactivate(clock.Today);
            }

            routines.Add(replacement);
            await unitOfWork.SaveChangesAsync(innerToken);
        }, cancellationToken);

        logger.LogInformation(
            "Rutina {PreviousId} reemplazada por {RoutineId} al cambiar el modo de calendario a {Mode}",
            previous.Id, replacement.Id, mode);

        await events.PublishAsync(
            new RoutineAssigned(replacement.Id, previous.MemberId, gymId, previous.SelfAssigned), cancellationToken);

        return await RoutineDrafting.BuildResponseAsync(exercises, equipment, replacement, cancellationToken);
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
    ICurrentUserService currentUser,
    IExerciseRepository exercises,
    ExerciseEquipmentResolver equipment,
    IRoutineRepository routines)
{
    public async Task<RoutineResponse> HandleAsync(Guid routineId, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var routine = await routines.GetWithDaysAsync(routineId, cancellationToken)
            ?? throw new NotFoundException("Rutina no encontrada.");

        return await RoutineDrafting.BuildResponseAsync(exercises, equipment, routine, cancellationToken);
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

/// <summary>Rutina activa del miembro + qué toca hoy, resolviendo la posición del ciclo.</summary>
public sealed class GetMyRoutineHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    IMemberReader members,
    IExerciseRepository exercises,
    ExerciseEquipmentResolver equipment,
    IRoutineRepository routines,
    IRoutineDayOverrideRepository overrides,
    MissedSession.MissedSessionService missed)
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

        var names = await exercises.GetVisibleSummariesAsync(
            RoutineMapper.ExerciseIds(routine), cancellationToken);
        var machines = await equipment.ResolveAsync(RoutineMapper.EquipmentNeeds(routine, names), cancellationToken);
        var response = RoutineMapper.ToResponse(routine, names, machines);

        var today = clock.Today;
        var (day, planned) = await RoutineSchedule.DayOnWithOverrideAsync(routine, today, overrides, self.Id, cancellationToken);

        // Semanal sin fila para hoy = descanso: no hay RoutineDay al que apuntar.
        var todayView = day is null
            ? new TodayView(today, 0, "Descanso", true, [], IsoWeekDay.Of(today))
            : new TodayView(
                today,
                day.Position,
                RoutineMapper.DayName(day),
                day.IsRest,
                day.Exercises.OrderBy(e => e.Order)
                    .Select(e => RoutineMapper.ToExerciseResponse(e, names, machines)).ToList(),
                IsoWeekDay.Of(today),
                RoutineMapper.MuscleGroupsOf(day, names),
                day.Id,
                planned is null ? null : RoutineMapper.DayName(planned));

        var missedSession = await missed.ViewAsync(
            self.Id, today, routine.AssignedBy, routine.SelfAssigned, cancellationToken);
        return new MyRoutineResponse(response, todayView, missedSession);
    }
}
