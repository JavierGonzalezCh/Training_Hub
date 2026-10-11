using GSM.Domain.Communications;

namespace GSM.Application.Communications.Abstractions;

public interface ICommunicationsSettingsRepository
{
    Task<CommunicationsSettings?> GetAsync(Guid gymId, CancellationToken cancellationToken = default);

    void Add(CommunicationsSettings settings);
}
