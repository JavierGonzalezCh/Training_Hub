using GSM.Application.Attendance.Abstractions;
using GSM.Domain.Attendance;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Attendance;

public sealed class MemberEntryRuleRepository(GsmDbContext dbContext) : IMemberEntryRuleRepository
{
    private DbSet<MemberEntryRule> Rules => dbContext.Set<MemberEntryRule>();

    public Task<MemberEntryRule?> GetForMemberAsync(Guid memberId, CancellationToken cancellationToken = default)
        => Rules.FirstOrDefaultAsync(r => r.MemberId == memberId, cancellationToken);

    public void Add(MemberEntryRule rule) => Rules.Add(rule);

    public void Remove(MemberEntryRule rule) => Rules.Remove(rule);
}
