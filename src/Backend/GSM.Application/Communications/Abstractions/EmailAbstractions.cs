using GSM.Domain.Communications;

namespace GSM.Application.Communications.Abstractions;

public interface IMemberCommunicationPreferencesRepository
{
    Task<MemberCommunicationPreferences?> GetAsync(Guid memberId, CancellationToken cancellationToken = default);

    void Add(MemberCommunicationPreferences preferences);

    /// <summary>De <paramref name="memberIds"/>, los que se dieron de baja del email.</summary>
    Task<IReadOnlySet<Guid>> GetOptedOutAsync(IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken = default);
}

public interface ICommunicationsSettingsRepository
{
    Task<CommunicationsSettings?> GetAsync(Guid gymId, CancellationToken cancellationToken = default);

    void Add(CommunicationsSettings settings);
}

/// <summary>
/// Enlace de baja de los correos: token firmado que funciona sin iniciar sesión. Implementado en
/// Infrastructure (HMAC con <c>Communications:UnsubscribeSigningKey</c>).
/// </summary>
public interface IUnsubscribeLinks
{
    string BuildUrl(Guid memberId, Guid gymId);

    bool TryRead(string? token, out Guid memberId, out Guid gymId);
}
