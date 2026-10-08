using GSM.Application.Communications.Abstractions;
using GSM.Application.Communications.Public;

namespace GSM.Application.Communications;

internal sealed class CommunicationPreferencesReader(
    IMemberCommunicationPreferencesRepository preferences,
    IUnsubscribeLinks unsubscribeLinks) : ICommunicationPreferencesReader
{
    public Task<IReadOnlySet<Guid>> GetEmailOptedOutAsync(
        IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken = default)
        => preferences.GetOptedOutAsync(memberIds, cancellationToken);

    public string BuildUnsubscribeUrl(Guid memberId, Guid gymId) => unsubscribeLinks.BuildUrl(memberId, gymId);
}
