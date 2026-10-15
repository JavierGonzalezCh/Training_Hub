using GSM.Application.Payments.Abstractions;
using GSM.Domain.Payments;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Payments;

public sealed class CashShiftRepository(GsmDbContext dbContext) : ICashShiftRepository
{
    private DbSet<CashShift> Shifts => dbContext.Set<CashShift>();

    private DbSet<CashMovement> Movements => dbContext.Set<CashMovement>();

    public void Add(CashShift shift) => Shifts.Add(shift);

    public void Add(CashMovement movement) => Movements.Add(movement);

    public Task<CashShift?> GetOpenAsync(CancellationToken cancellationToken = default)
        => Shifts.FirstOrDefaultAsync(s => s.ClosedAt == null, cancellationToken);

    public Task<CashShift?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => Shifts.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<CashShift?> GetLastClosedAsync(CancellationToken cancellationToken = default)
        => Shifts.AsNoTracking()
            .Where(s => s.ClosedAt != null)
            .OrderByDescending(s => s.ClosedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlySet<Guid>> GetOpenShiftIdsAsync(
        IReadOnlyCollection<Guid> shiftIds, CancellationToken cancellationToken = default)
    {
        if (shiftIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var ids = await Shifts.AsNoTracking()
            .Where(s => s.ClosedAt == null && shiftIds.Contains(s.Id))
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);
        return ids.ToHashSet();
    }

    public async Task<(IReadOnlyList<CashShift> Items, int Total)> GetPageAsync(
        DateTimeOffset? fromUtc, DateTimeOffset? toUtc, Guid? openedBy, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        var q = Shifts.AsNoTracking().AsQueryable();
        if (fromUtc is { } from)
        {
            q = q.Where(s => s.OpenedAt >= from);
        }

        if (toUtc is { } to)
        {
            q = q.Where(s => s.OpenedAt < to);
        }

        if (openedBy is { } by)
        {
            q = q.Where(s => s.OpenedBy == by);
        }

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderByDescending(s => s.OpenedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<IReadOnlyList<CashShift>> GetOpenedInRangeAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
        => await Shifts.AsNoTracking()
            .Where(s => s.OpenedAt >= fromUtc && s.OpenedAt < toUtc)
            .OrderBy(s => s.OpenedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CashMovement>> GetMovementsAsync(
        IReadOnlyCollection<Guid> shiftIds, CancellationToken cancellationToken = default)
    {
        if (shiftIds.Count == 0)
        {
            return [];
        }

        return await Movements.AsNoTracking()
            .Where(m => shiftIds.Contains(m.ShiftId))
            .OrderBy(m => m.OccurredAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<(decimal Deposits, decimal Withdrawals)> GetMovementTotalsAsync(
        Guid shiftId, CancellationToken cancellationToken = default)
    {
        var rows = await Movements.AsNoTracking()
            .Where(m => m.ShiftId == shiftId)
            .GroupBy(m => m.Type)
            .Select(g => new { Type = g.Key, Total = g.Sum(m => m.Amount) })
            .ToListAsync(cancellationToken);
        return (
            rows.FirstOrDefault(r => r.Type == CashMovementType.Deposit)?.Total ?? 0m,
            rows.FirstOrDefault(r => r.Type == CashMovementType.Withdrawal)?.Total ?? 0m);
    }
}
