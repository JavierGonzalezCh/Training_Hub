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

/// <summary>Contexto del socio para los endpoints de seguimiento: valida el feature flag y resuelve su id.</summary>
internal static class RoutineMemberContext
{
    public static async Task<Guid> ResolveAsync(
        ICurrentUserService currentUser, IGymReader gyms, IMemberReader members, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        await RoutineFeatureGate.EnsureMemberModuleEnabledAsync(gyms, gymId, cancellationToken);

        var self = await members.ResolveByUserIdAsync(currentUser.UserId ?? Guid.Empty, cancellationToken)
            ?? throw new ForbiddenDomainException("Tu cuenta no está vinculada a un miembro.");

        return self.Id;
    }
}

public sealed class LogRoutineExerciseHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    IMemberReader members,
    IRoutineLogRepository logs,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<LogRoutineExerciseHandler> logger)
{
    public async Task<RoutineLogResponse> HandleAsync(
        Guid routineExerciseId, LogRoutineExerciseRequest request, CancellationToken cancellationToken)
    {
        var memberId = await RoutineMemberContext.ResolveAsync(currentUser, gyms, members, cancellationToken);

        var reference = await logs.GetExerciseRefAsync(routineExerciseId, cancellationToken)
            ?? throw new NotFoundException("Ejercicio de rutina no encontrado.");

        if (reference.MemberId != memberId)
        {
            throw new ForbiddenDomainException("No puedes registrar sobre la rutina de otro socio.");
        }

        var today = clock.Today;
        var now = clock.UtcNow;
        var sets = request.Sets?.Select(s => new RoutineSetEntry(0, s.Reps, s.WeightKg)).ToList();

        var existing = await logs.GetAsync(routineExerciseId, today, cancellationToken);

        // "Primero del día" se mide dentro del mismo tipo de rutina: las auto-asignadas no
        // puntúan, así que no deben consumir el evento de las asignadas (ni al revés).
        var firstOfDay = existing is null
            && !await logs.MemberHasLogsOnAsync(
                memberId, today, reference.SelfAssigned, cancellationToken);

        RoutineExerciseLog log;
        if (existing is not null)
        {
            existing.Update(now, request.SetsDone, request.RepsDone, request.WeightKg, request.Notes, sets);
            log = existing;
        }
        else
        {
            log = RoutineExerciseLog.Record(
                reference.GymId, memberId, routineExerciseId, today, now,
                request.SetsDone, request.RepsDone, request.WeightKg, request.Notes, sets);
            logs.Add(log);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (firstOfDay)
        {
            logger.LogInformation(
                "Socio {MemberId} inició sesión de rutina el {Date}", memberId, today);
            await events.PublishAsync(
                new RoutineSessionLogged(memberId, reference.GymId, today, reference.SelfAssigned),
                cancellationToken);
        }

        return new RoutineLogResponse(
            log.Id, log.RoutineExerciseId, log.PerformedOn,
            log.SetsDone, log.RepsDone, log.WeightKg, log.Notes, log.CompletedAt,
            log.Sets.Select(s => new SetResponse(s.Number, s.Reps, s.WeightKg)).ToList());
    }
}

public sealed class UnlogRoutineExerciseHandler(
    ICurrentUserService currentUser,
    IGymReader gyms,
    IMemberReader members,
    IRoutineLogRepository logs,
    IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(
        Guid routineExerciseId, DateOnly date, CancellationToken cancellationToken)
    {
        var memberId = await RoutineMemberContext.ResolveAsync(currentUser, gyms, members, cancellationToken);

        var reference = await logs.GetExerciseRefAsync(routineExerciseId, cancellationToken)
            ?? throw new NotFoundException("Ejercicio de rutina no encontrado.");

        if (reference.MemberId != memberId)
        {
            throw new ForbiddenDomainException("No puedes registrar sobre la rutina de otro socio.");
        }

        var existing = await logs.GetAsync(routineExerciseId, date, cancellationToken);
        if (existing is null)
        {
            return;
        }

        logs.Remove(existing);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class GetMyRoutineHistoryHandler(
    ICurrentUserService currentUser,
    IGymReader gyms,
    IMemberReader members,
    IRoutineLogRepository logs)
{
    public async Task<IReadOnlyList<RoutineHistoryItem>> HandleAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var memberId = await RoutineMemberContext.ResolveAsync(currentUser, gyms, members, cancellationToken);

        if (to < from)
        {
            (from, to) = (to, from);
        }

        var rows = await logs.GetHistoryAsync(memberId, from, to, cancellationToken);
        return rows
            .Select(r => new RoutineHistoryItem(
                r.PerformedOn, r.RoutineExerciseId, r.ExerciseId, r.ExerciseName,
                r.SetsDone, r.RepsDone, r.WeightKg, r.Notes, r.CompletedAt,
                r.Sets?.Select(s => new SetResponse(s.Number, s.Reps, s.WeightKg)).ToList()))
            .ToList();
    }
}
