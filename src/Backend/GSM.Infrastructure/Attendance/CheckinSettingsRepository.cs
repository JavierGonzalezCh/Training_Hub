using GSM.Application.Attendance.Abstractions;
using GSM.Domain.Attendance;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Attendance;

public sealed class CheckinSettingsRepository(GsmDbContext dbContext) : ICheckinSettingsRepository
{
    private DbSet<CheckinSettings> Settings => dbContext.Set<CheckinSettings>();

    public Task<CheckinSettings?> GetAsync(Guid gymId, CancellationToken cancellationToken = default)
        => Settings.FirstOrDefaultAsync(s => s.GymId == gymId, cancellationToken);

    public void Add(CheckinSettings settings) => Settings.Add(settings);
}
