using GSM.Domain.Communications;

namespace GSM.Application.Communications.Abstractions;

public interface ICommunicationsSettingsRepository
{
    Task<CommunicationsSettings?> GetAsync(Guid gymId, CancellationToken cancellationToken = default);

    void Add(CommunicationsSettings settings);

    /// <summary>Gyms que encendieron la felicitación de cumpleaños. Para la tarea periódica, que corre sin usuario.</summary>
    Task<IReadOnlyList<CommunicationsSettings>> ListBirthdayEnabledAsync(CancellationToken cancellationToken = default);
}
