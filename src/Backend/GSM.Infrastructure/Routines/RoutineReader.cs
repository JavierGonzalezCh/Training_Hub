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
        Guid memberId, CancellationToken cancellationToken = default)
    {
        var starts = await Routines
            .Where(r => r.MemberId == memberId && r.IsActive)
            .OrderByDescending(r => r.StartsOn)
            .Select(r => (DateOnly?)r.StartsOn)
            .FirstOrDefaultAsync(cancellationToken);

        return starts;
    }

    public async Task<RoutineTrainingCalendar> GetTrainingCalendarAsync(
        Guid memberId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var training = new HashSet<DateOnly>();
        var rest = new HashSet<DateOnly>();

        if (to < from)
        {
            return new RoutineTrainingCalendar(training, rest);
        }

        // Rutinas del socio que se solapan con [from, to].
        var routines = await Routines
            .Include(r => r.Days)
            .Where(r => r.MemberId == memberId
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
            // Rutina vigente ese día: la de starts_on más reciente que aún lo cubre.
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
}
