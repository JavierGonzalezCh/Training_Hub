using GSM.Domain.Attendance;

namespace GSM.Application.Attendance.Abstractions;

public interface IMemberEntryRuleRepository
{
    Task<MemberEntryRule?> GetForMemberAsync(Guid memberId, CancellationToken cancellationToken = default);

    void Add(MemberEntryRule rule);

    void Remove(MemberEntryRule rule);
}
