using GSM.Application.Common.Abstractions;
using GSM.Application.Routines;
using GSM.Application.Routines.Abstractions;
using GSM.Application.Routines.Public;
using GSM.Domain.Routines;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Routines;

/// <summary>
/// Lectura para el compositor de racha y para Achievements. Respeta el Global Query Filter:
/// el llamador siempre opera en su gym.
/// </summary>
public sealed class RoutineReader(GsmDbContext dbContext, IClock clock) : IRoutineReader
{
    private IQueryable<Routine> Routines => dbContext.Set<Routine>();

    public Task<bool> HasSettingsAsync(Guid gymId, CancellationToken cancellationToken = default)
        => dbContext.Set<RoutineSettings>().IgnoreQueryFilters().AnyAsync(s => s.GymId == gymId, cancellationToken);

    public Task<int> GetActiveRoutineCountAsync(Guid memberId, CancellationToken cancellationToken = default)
        => Routines.CountAsync(r => r.MemberId == memberId && r.IsActive, cancellationToken);

    public async Task<DateOnly?> GetActiveRoutineStartAsync(
        Guid memberId, bool onlyAssigned = false, CancellationToken cancellationToken = default)
    {
        var starts = await Routines
            .Where(r => r.MemberId == memberId && r.IsActive && (!onlyAssigned || !r.SelfAssigned))
            .OrderByDescending(r => r.StartsOn)
            .Select(r => (DateOnly?)r.StartsOn)
            .FirstOrDefaultAsync(cancellationToken);

        return starts;
    }

    public async Task<DateOnly?> GetCalendarStartAsync(
        Guid memberId, bool onlyAssigned = false, CancellationToken cancellationToken = default)
    {
        var mine = Routines.Where(r => r.MemberId == memberId && (!onlyAssigned || !r.SelfAssigned));
        if (!await mine.AnyAsync(r => r.IsActive, cancellationToken))
        {
            return null;
        }

        return await mine.MinAsync(r => (DateOnly?)r.StartsOn, cancellationToken);
    }

    public async Task<RoutineTrainingCalendar> GetTrainingCalendarAsync(
        Guid memberId, DateOnly from, DateOnly to,
        bool onlyAssigned = false, CancellationToken cancellationToken = default)
    {
        var training = new HashSet<DateOnly>();
        var rest = new HashSet<DateOnly>();

        if (to < from)
        {
            return new RoutineTrainingCalendar(training, rest);
        }

        var routines = await Routines
            .Where(r => r.MemberId == memberId
                        && (!onlyAssigned || !r.SelfAssigned)
                        && r.StartsOn <= to
                        && (r.DeactivatedOn == null || r.DeactivatedOn >= from))
            .OrderByDescending(r => r.StartsOn)
            .ThenByDescending(r => r.IsActive)
            .ToListAsync(cancellationToken);

        if (routines.Count == 0)
        {
            return new RoutineTrainingCalendar(training, rest);
        }

        // R2b: todos los días, también los retirados; cada fecha se juzga con los que estaban vigentes ese día.
        var routineIds = routines.Select(r => r.Id).ToList();
        var allDays = await dbContext.Set<RoutineDay>()
            .AsNoTracking()
            .Where(d => routineIds.Contains(d.RoutineId))
            .ToListAsync(cancellationToken);
        var daysByRoutine = allDays.ToLookup(d => d.RoutineId);

        var ordered = RoutineCalendar.Order(routines.Select(r => new AdherenceRoutine(
            r.MemberId, r.Id, r.Name, r.SelfAssigned, r.ScheduleMode, r.StartsOn, r.DeactivatedOn, r.IsActive,
            daysByRoutine[r.Id]
                .Select(d => new AdherenceDay(d.Id, d.Position, d.IsRest, d.DayOfWeek, d.AddedOn, d.RetiredOn, d.Name, []))
                .ToList())));

        // R2b: cada fecha se juzga con lo que estaba vigente ese día (regla única en RoutineCalendar). Los ajustes del
        // miembro (SP) no cambian qué fechas son de entrenamiento o descanso, solo qué día cuenta: la racha no los necesita.
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var resolution = RoutineCalendar.Resolve(ordered, date);
            if (resolution.IsTraining)
            {
                training.Add(date);
            }
            else if (resolution.IsPlannedRest)
            {
                rest.Add(date);
            }
        }

        return new RoutineTrainingCalendar(training, rest);
    }

    public async Task<int> GetCompletedSessionCountAsync(
        Guid memberId, bool onlyAssigned, CancellationToken cancellationToken = default)
    {
        var logs = dbContext.Set<RoutineExerciseLog>().Where(l => l.MemberId == memberId);

        if (onlyAssigned)
        {
            var assignedDates =
                from log in logs
                join ex in dbContext.Set<RoutineExercise>() on log.RoutineExerciseId equals ex.Id
                join day in dbContext.Set<RoutineDay>() on ex.RoutineDayId equals day.Id
                join routine in dbContext.Set<Routine>() on day.RoutineId equals routine.Id
                where !routine.SelfAssigned
                select log.PerformedOn;

            return await assignedDates.Distinct().CountAsync(cancellationToken);
        }

        return await logs.Select(l => l.PerformedOn).Distinct().CountAsync(cancellationToken);
    }

    public async Task<LiftingStats> GetLiftingStatsAsync(
        Guid memberId, bool onlyAssigned, CancellationToken cancellationToken = default)
    {
        // Las series son una colección JSON propia: proyectarla exige no rastrear.
        var rows = await (from log in dbContext.Set<RoutineExerciseLog>().AsNoTracking()
                          where log.MemberId == memberId
                          join ex in dbContext.Set<RoutineExercise>() on log.RoutineExerciseId equals ex.Id
                          join day in dbContext.Set<RoutineDay>() on ex.RoutineDayId equals day.Id
                          join routine in dbContext.Set<Routine>() on day.RoutineId equals routine.Id
                          where !onlyAssigned || !routine.SelfAssigned
                          select new { log.ExerciseId, log.PerformedOn, routine.SelfAssigned, log.Sets })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return new LiftingStats(0, 0m);
        }

        var ids = rows.Select(r => r.ExerciseId).Distinct().ToList();
        var repsWeight = (await dbContext.Set<Exercise>()
                .Where(e => ids.Contains(e.Id) && e.TrackingType == TrackingType.RepsWeight)
                .Select(e => e.Id)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        return LiftingStatsCalculator.Compute(rows
            .Where(r => repsWeight.Contains(r.ExerciseId))
            .Select(r => (r.ExerciseId, new ProgressLog(r.PerformedOn, r.SelfAssigned, r.Sets))));
    }

    public async Task<DueRoutinesCount> GetDueForReviewCountAsync(
        IReadOnlyCollection<Guid>? memberIds, int windowDays, CancellationToken cancellationToken = default)
    {
        var today = clock.Today;
        var until = today.AddDays(windowDays);

        var query = Routines.Where(r => r.IsActive && !r.SelfAssigned && r.ReviewOn <= until);
        if (memberIds is not null)
        {
            query = query.Where(r => memberIds.Contains(r.MemberId));
        }

        var due = await query.CountAsync(cancellationToken);
        var overdue = await query.CountAsync(r => r.ReviewOn < today, cancellationToken);
        return new DueRoutinesCount(due, overdue);
    }

    public async Task<IReadOnlySet<Guid>> GetMemberIdsWithActiveRoutineAsync(
        IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken = default)
    {
        if (memberIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        return (await Routines
                .Where(r => r.IsActive && memberIds.Contains(r.MemberId))
                .Select(r => r.MemberId)
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet();
    }
}
