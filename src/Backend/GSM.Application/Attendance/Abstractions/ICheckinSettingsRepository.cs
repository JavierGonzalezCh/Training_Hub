using GSM.Domain.Attendance;

namespace GSM.Application.Attendance.Abstractions;

public interface ICheckinSettingsRepository
{
    Task<CheckinSettings?> GetAsync(Guid gymId, CancellationToken cancellationToken = default);

    void Add(CheckinSettings settings);
}
