using GSM.Domain.Communications;

namespace GSM.Application.Communications.Abstractions;

public interface IMemberFeedStateRepository
{
    Task<MemberFeedState?> GetAsync(Guid memberId, CancellationToken cancellationToken = default);

    void Add(MemberFeedState state);
}
