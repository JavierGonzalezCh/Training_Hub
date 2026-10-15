using GSM.Application.Classes.Abstractions;
using GSM.Domain.Classes;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Classes;

public sealed class ClassSettingsRepository(GsmDbContext dbContext) : IClassSettingsRepository
{
    public Task<ClassSettings?> GetAsync(Guid gymId, CancellationToken cancellationToken = default)
        => dbContext.Set<ClassSettings>().FirstOrDefaultAsync(s => s.GymId == gymId, cancellationToken);

    public void Add(ClassSettings settings) => dbContext.Set<ClassSettings>().Add(settings);
}

public sealed class BookingBlockRepository(GsmDbContext dbContext) : IBookingBlockRepository
{
    private DbSet<BookingBlock> Blocks => dbContext.Set<BookingBlock>();

    public Task<BookingBlock?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => Blocks.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    public Task<BookingBlock?> GetActiveAsync(Guid memberId, DateOnly today, CancellationToken cancellationToken = default)
        => Blocks
            .Where(b => b.MemberId == memberId && b.LiftedAt == null && b.BlockedUntil >= today)
            .OrderByDescending(b => b.BlockedUntil)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<DateTimeOffset?> GetLastAppliedAtAsync(Guid memberId, CancellationToken cancellationToken = default)
        => await Blocks
            .Where(b => b.MemberId == memberId)
            .OrderByDescending(b => b.AppliedAt)
            .Select(b => (DateTimeOffset?)b.AppliedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<BookingBlock>> ListActiveAsync(DateOnly today, CancellationToken cancellationToken = default)
        => await Blocks
            .Where(b => b.LiftedAt == null && b.BlockedUntil >= today)
            .OrderBy(b => b.BlockedUntil)
            .ToListAsync(cancellationToken);

    public void Add(BookingBlock block) => Blocks.Add(block);
}
