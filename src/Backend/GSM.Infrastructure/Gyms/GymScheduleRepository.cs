using GSM.Application.Gyms.Abstractions;
using GSM.Domain.Gyms;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Gyms;

public sealed class GymScheduleRepository(GsmDbContext dbContext) : IGymScheduleRepository
{
    public async Task<IReadOnlyList<GymOpeningSlot>> GetWeeklySlotsAsync(CancellationToken cancellationToken = default)
        => await dbContext.Set<GymOpeningSlot>().OrderBy(s => s.Day).ThenBy(s => s.OpensAt).ToListAsync(cancellationToken);

    public void Add(GymOpeningSlot slot) => dbContext.Set<GymOpeningSlot>().Add(slot);

    public void Remove(GymOpeningSlot slot) => dbContext.Set<GymOpeningSlot>().Remove(slot);

    public Task<GymSpecialDay?> GetSpecialDayAsync(DateOnly date, CancellationToken cancellationToken = default)
        => dbContext.Set<GymSpecialDay>().Include(d => d.Slots).FirstOrDefaultAsync(d => d.Date == date, cancellationToken);

    public async Task<IReadOnlyList<GymSpecialDay>> ListSpecialDaysAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        => await dbContext.Set<GymSpecialDay>().Include(d => d.Slots)
            .Where(d => d.Date >= from && d.Date <= to)
            .OrderBy(d => d.Date)
            .ToListAsync(cancellationToken);

    public void Add(GymSpecialDay specialDay) => dbContext.Set<GymSpecialDay>().Add(specialDay);

    public void Remove(GymSpecialDay specialDay) => dbContext.Set<GymSpecialDay>().Remove(specialDay);
}
