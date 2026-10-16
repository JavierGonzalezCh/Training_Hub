using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms.Public;
using GSM.Application.Members.Public;
using GSM.Application.Routines.Abstractions;
using GSM.Application.Routines.Contracts;
using GSM.Domain.Common.Exceptions;

namespace GSM.Application.Routines;

/// <summary>
/// Progreso por ejercicio (R7), leído de <c>routine_exercise_logs</c> por <c>exercise_id</c>: sobrevive a cambios y
/// retiros de rutina y se suma por el ejercicio que de verdad se hizo. Solo lectura.
/// </summary>
public sealed class MemberProgressService(IRoutineLogRepository logs, IExerciseRepository exercises)
{
    public const int MaxPoints = 200;

    public async Task<IReadOnlyList<ExerciseProgressSummary>> ListAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var rows = await logs.GetProgressLogsAsync(memberId, null, cancellationToken);
        var byExercise = rows.GroupBy(r => r.ExerciseId).ToList();
        var summaries = await exercises.GetVisibleSummariesAsync(
            byExercise.Select(g => g.Key).ToList(), cancellationToken);

        var result = new List<ExerciseProgressSummary>();
        foreach (var group in byExercise)
        {
            if (!summaries.TryGetValue(group.Key, out var exercise))
            {
                continue;
            }

            var points = MemberProgressCalculator.Points(group.Select(r => new ProgressLog(r.PerformedOn, r.SelfAssigned, r.Sets)));
            result.Add(new ExerciseProgressSummary(
                group.Key,
                exercise.Name,
                exercise.TrackingType.ToString(),
                exercise.IsUnilateral,
                group.Select(r => r.PerformedOn).Distinct().Count(),
                group.Max(r => r.PerformedOn),
                MemberProgressCalculator.Records(exercise.TrackingType, points)));
        }

        return result.OrderByDescending(s => s.LastPerformedOn).ThenBy(s => s.ExerciseName).ToList();
    }

    public async Task<ExerciseProgressResponse> GetAsync(
        Guid memberId, Guid exerciseId, CancellationToken cancellationToken)
    {
        var summaries = await exercises.GetVisibleSummariesAsync([exerciseId], cancellationToken);
        if (!summaries.TryGetValue(exerciseId, out var exercise))
        {
            throw new NotFoundException("Ejercicio no encontrado.");
        }

        var rows = await logs.GetProgressLogsAsync(memberId, exerciseId, cancellationToken);
        var points = MemberProgressCalculator.Points(rows.Select(r => new ProgressLog(r.PerformedOn, r.SelfAssigned, r.Sets)));

        return new ExerciseProgressResponse(
            exerciseId,
            exercise.Name,
            exercise.TrackingType.ToString(),
            exercise.IsUnilateral,
            rows.Select(r => r.PerformedOn).Distinct().Count(),
            points.Count > MaxPoints ? points.Skip(points.Count - MaxPoints).ToList() : points,
            MemberProgressCalculator.Records(exercise.TrackingType, points));
    }
}

/// <summary>Los ejercicios que el miembro ya registró, con sus récords (app del miembro).</summary>
public sealed class GetMyProgressHandler(
    ICurrentUserService currentUser, IGymReader gyms, IMemberReader members, MemberProgressService progress)
{
    public async Task<IReadOnlyList<ExerciseProgressSummary>> HandleAsync(CancellationToken cancellationToken)
    {
        var memberId = await RoutineMemberContext.ResolveAsync(currentUser, gyms, members, cancellationToken);
        return await progress.ListAsync(memberId, cancellationToken);
    }
}

public sealed class GetMyExerciseProgressHandler(
    ICurrentUserService currentUser, IGymReader gyms, IMemberReader members, MemberProgressService progress)
{
    public async Task<ExerciseProgressResponse> HandleAsync(Guid exerciseId, CancellationToken cancellationToken)
    {
        var memberId = await RoutineMemberContext.ResolveAsync(currentUser, gyms, members, cancellationToken);
        return await progress.GetAsync(memberId, exerciseId, cancellationToken);
    }
}

/// <summary>Lo mismo para el staff, en la ficha del miembro (pestaña Rutina).</summary>
public sealed class GetMemberProgressHandler(
    ICurrentUserService currentUser, IMemberReader members, MemberProgressService progress)
{
    public async Task<IReadOnlyList<ExerciseProgressSummary>> HandleAsync(Guid memberId, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        if (!await members.ExistsAsync(memberId, cancellationToken))
        {
            throw new NotFoundException("Miembro no encontrado.");
        }

        return await progress.ListAsync(memberId, cancellationToken);
    }
}

public sealed class GetMemberExerciseProgressHandler(
    ICurrentUserService currentUser, IMemberReader members, MemberProgressService progress)
{
    public async Task<ExerciseProgressResponse> HandleAsync(
        Guid memberId, Guid exerciseId, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        if (!await members.ExistsAsync(memberId, cancellationToken))
        {
            throw new NotFoundException("Miembro no encontrado.");
        }

        return await progress.GetAsync(memberId, exerciseId, cancellationToken);
    }
}
