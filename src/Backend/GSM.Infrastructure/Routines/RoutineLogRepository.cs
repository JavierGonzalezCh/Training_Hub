using GSM.Application.Routines.Abstractions;
using GSM.Domain.Routines;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Routines;

public sealed class RoutineLogRepository(GsmDbContext dbContext) : IRoutineLogRepository
{
    private DbSet<RoutineExerciseLog> Logs => dbContext.Set<RoutineExerciseLog>();

    public Task<RoutineExerciseRef?> GetExerciseRefAsync(
        Guid routineExerciseId, CancellationToken cancellationToken = default)
        => (from ex in dbContext.Set<RoutineExercise>()
            join day in dbContext.Set<RoutineDay>() on ex.RoutineDayId equals day.Id
            join routine in dbContext.Set<Routine>() on day.RoutineId equals routine.Id
            where ex.Id == routineExerciseId
            select new RoutineExerciseRef(ex.Id, routine.GymId, routine.MemberId, routine.SelfAssigned))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<RoutineExerciseLog?> GetAsync(
        Guid routineExerciseId, DateOnly performedOn, CancellationToken cancellationToken = default)
        => Logs.FirstOrDefaultAsync(
            l => l.RoutineExerciseId == routineExerciseId && l.PerformedOn == performedOn, cancellationToken);

    public Task<bool> MemberHasLogsOnAsync(
        Guid memberId, DateOnly performedOn, bool selfAssigned, CancellationToken cancellationToken = default)
        => (from log in Logs
            where log.MemberId == memberId && log.PerformedOn == performedOn
            join ex in dbContext.Set<RoutineExercise>() on log.RoutineExerciseId equals ex.Id
            join day in dbContext.Set<RoutineDay>() on ex.RoutineDayId equals day.Id
            join routine in dbContext.Set<Routine>() on day.RoutineId equals routine.Id
            where routine.SelfAssigned == selfAssigned
            select log.Id)
            .AnyAsync(cancellationToken);

    public async Task<IReadOnlyList<RoutineHistoryRow>> GetHistoryAsync(
        Guid memberId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default)
        => await (from log in Logs.AsNoTracking() // las series son una colección JSON propia: proyectarla exige no rastrear
                  where log.MemberId == memberId && log.PerformedOn >= fromDate && log.PerformedOn <= toDate
                  join ex in dbContext.Set<RoutineExercise>() on log.RoutineExerciseId equals ex.Id
                  join catalog in dbContext.Set<Exercise>() on ex.ExerciseId equals catalog.Id into c
                  from catalog in c.DefaultIfEmpty()
                  orderby log.PerformedOn descending, ex.Order
                  select new RoutineHistoryRow(
                      log.PerformedOn,
                      log.RoutineExerciseId,
                      ex.ExerciseId,
                      catalog != null ? catalog.Name : "?",
                      log.SetsDone,
                      log.RepsDone,
                      log.WeightKg,
                      log.Notes,
                      log.CompletedAt,
                      log.Sets))
            .ToListAsync(cancellationToken);

    public void Add(RoutineExerciseLog log) => Logs.Add(log);

    public void Remove(RoutineExerciseLog log) => Logs.Remove(log);
}
