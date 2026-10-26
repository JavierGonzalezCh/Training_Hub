using GSM.Application.Routines.Abstractions;
using GSM.Domain.Routines;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Routines;

/// <summary>Lectura plana para el cumplimiento de la rutina. Respeta el Global Query Filter: opera en el gym del llamador.</summary>
public sealed class AdherenceSource(GsmDbContext dbContext) : IAdherenceSource
{
    public async Task<AdherenceData> LoadAsync(
        IReadOnlyCollection<Guid> memberIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (memberIds.Count == 0 || to < from)
        {
            return new AdherenceData([], []);
        }

        var routines = await dbContext.Set<Routine>().AsNoTracking()
            .Where(r => memberIds.Contains(r.MemberId)
                        && r.StartsOn <= to
                        && (r.DeactivatedOn == null || r.DeactivatedOn >= from))
            .ToListAsync(cancellationToken);

        var routineIds = routines.Select(r => r.Id).ToList();
        var days = await dbContext.Set<RoutineDay>().AsNoTracking()
            .Where(d => routineIds.Contains(d.RoutineId))
            .ToListAsync(cancellationToken);
        var dayIds = days.Select(d => d.Id).ToList();
        var lines = await dbContext.Set<RoutineExercise>().AsNoTracking()
            .Where(e => dayIds.Contains(e.RoutineDayId))
            .Select(e => new { e.Id, e.RoutineDayId, e.RetiredOn })
            .ToListAsync(cancellationToken);

        var linesByDay = lines.ToLookup(l => l.RoutineDayId, l => new AdherenceLine(l.Id, l.RetiredOn));
        var daysByRoutine = days.ToLookup(
            d => d.RoutineId,
            d => new AdherenceDay(
                d.Id, d.Position, d.IsRest, d.DayOfWeek, d.AddedOn, d.RetiredOn, d.Name, linesByDay[d.Id].ToList()));

        var logs = await dbContext.Set<RoutineExerciseLog>().AsNoTracking()
            .Where(l => memberIds.Contains(l.MemberId) && l.PerformedOn >= from && l.PerformedOn <= to)
            .Select(l => new AdherenceLog(l.MemberId, l.PerformedOn, l.RoutineExerciseId))
            .ToListAsync(cancellationToken);

        return new AdherenceData(
            routines.Select(r => new AdherenceRoutine(
                r.MemberId, r.Id, r.Name, r.SelfAssigned, r.ScheduleMode, r.StartsOn, r.DeactivatedOn, r.IsActive,
                daysByRoutine[r.Id].ToList())).ToList(),
            logs);
    }

    public async Task<IReadOnlyDictionary<Guid, DateOnly>> GetLastLogDatesAsync(
        IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken = default)
    {
        if (memberIds.Count == 0)
        {
            return new Dictionary<Guid, DateOnly>();
        }

        var rows = await dbContext.Set<RoutineExerciseLog>().AsNoTracking()
            .Where(l => memberIds.Contains(l.MemberId))
            .GroupBy(l => l.MemberId)
            .Select(g => new { MemberId = g.Key, Last = g.Max(l => l.PerformedOn) })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.MemberId, r => r.Last);
    }
}
