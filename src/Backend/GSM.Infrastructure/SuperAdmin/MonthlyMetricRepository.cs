using GSM.Application.SuperAdmin.Abstractions;
using GSM.Domain.SuperAdmin;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.SuperAdmin;

public sealed class MonthlyMetricRepository(GsmDbContext dbContext) : IMonthlyMetricRepository
{
    private DbSet<MonthlyMetric> Metrics => dbContext.Set<MonthlyMetric>();

    public void Add(MonthlyMetric metric) => Metrics.Add(metric);

    public Task<bool> ExistsAsync(DateOnly month, CancellationToken cancellationToken = default)
        => Metrics.AnyAsync(m => m.Month == month, cancellationToken);

    public Task<MonthlyMetric?> GetAsync(DateOnly month, CancellationToken cancellationToken = default)
        => Metrics.AsNoTracking().FirstOrDefaultAsync(m => m.Month == month, cancellationToken);

    public async Task<IReadOnlyList<MonthlyMetric>> GetLatestAsync(int count, CancellationToken cancellationToken = default)
        => await Metrics.AsNoTracking().OrderByDescending(m => m.Month).Take(count).ToListAsync(cancellationToken);
}
