using GSM.Application.Achievements.Abstractions;
using GSM.Domain.Achievements;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Achievements;

public sealed class RankingRepository(GsmDbContext dbContext) : IRankingRepository
{
    private DbSet<RankingParticipant> Participants => dbContext.Set<RankingParticipant>();

    public async Task<IReadOnlyList<RankingParticipant>> ListAsync(CancellationToken cancellationToken = default)
        => await Participants.ToListAsync(cancellationToken);

    public Task<RankingParticipant?> GetByMemberAsync(Guid memberId, CancellationToken cancellationToken = default)
        => Participants.FirstOrDefaultAsync(p => p.MemberId == memberId, cancellationToken);

    public Task<bool> AliasTakenAsync(string alias, Guid exceptMemberId, CancellationToken cancellationToken = default)
    {
        var lowered = alias.ToLower();
        return Participants.AnyAsync(
            p => p.MemberId != exceptMemberId && p.Alias != null && p.Alias.ToLower() == lowered, cancellationToken);
    }

    public void Add(RankingParticipant participant) => Participants.Add(participant);

    public void Remove(RankingParticipant participant) => Participants.Remove(participant);
}
