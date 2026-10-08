using GSM.Application.Routines;
using GSM.Application.Routines.Public;
using GSM.Domain.Routines;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Routines;

/// <summary>
/// Lectura para el compositor de racha y para Achievements. Respeta el Global Query Filter:
/// el llamador siempre opera en su gym.
/// </summary>
public sealed class RoutineReader(GsmDbContext dbContext) : IRoutineReader
{
    private IQueryable<Routine> Routines => dbContext.Set<Routine>();

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
            .Include(r => r.Days.Where(d => d.RetiredOn == null))
            .Where(r => r.MemberId == memberId
                        && (!onlyAssigned || !r.SelfAssigned)
                        && r.StartsOn <= to
                        && (r.DeactivatedOn == null || r.DeactivatedOn >= from))
            .OrderByDescending(r => r.StartsOn)
            .ToListAsync(cancellationToken);

        if (routines.Count == 0)
        {
            return new RoutineTrainingCalendar(training, rest);
        }

        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var routine = routines.FirstOrDefault(r =>
                r.StartsOn <= date && (r.DeactivatedOn == null || date <= r.DeactivatedOn));

            if (routine is null || routine.CycleLength == 0)
            {
                continue;
            }

            var position = RoutineCycle.PositionOn(routine.StartsOn, routine.CycleLength, date);
            var day = routine.Days.FirstOrDefault(d => d.Position == position);
            if (day is null)
            {
                continue;
            }

            (day.IsRest ? rest : training).Add(date);
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
}
