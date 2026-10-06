using GSM.Application.Communications.Abstractions;
using GSM.Application.Communications.Contracts;
using GSM.Domain.Communications;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Communications;

public sealed class AnnouncementReactionRepository(GsmDbContext dbContext) : IAnnouncementReactionRepository
{
    private DbSet<AnnouncementReaction> Reactions => dbContext.Set<AnnouncementReaction>();

    public Task<AnnouncementReaction?> GetAsync(
        Guid announcementId, Guid memberId, CancellationToken cancellationToken = default)
        => Reactions.FirstOrDefaultAsync(r => r.AnnouncementId == announcementId && r.MemberId == memberId, cancellationToken);

    public void Add(AnnouncementReaction reaction) => Reactions.Add(reaction);

    public void Remove(AnnouncementReaction reaction) => Reactions.Remove(reaction);

    public async Task<IReadOnlyDictionary<Guid, ReactionCounts>> CountAsync(
        IReadOnlyCollection<Guid> announcementIds, CancellationToken cancellationToken = default)
    {
        if (announcementIds.Count == 0)
        {
            return new Dictionary<Guid, ReactionCounts>();
        }

        var rows = await Reactions.AsNoTracking()
            .Where(r => announcementIds.Contains(r.AnnouncementId))
            .GroupBy(r => new { r.AnnouncementId, r.Kind })
            .Select(g => new { g.Key.AnnouncementId, g.Key.Kind, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.AnnouncementId)
            .ToDictionary(
                g => g.Key,
                g => new ReactionCounts(
                    g.Where(r => r.Kind == ReactionKind.Like).Sum(r => r.Count),
                    g.Where(r => r.Kind == ReactionKind.Celebrate).Sum(r => r.Count),
                    g.Where(r => r.Kind == ReactionKind.Strong).Sum(r => r.Count)));
    }

    public async Task<IReadOnlyDictionary<Guid, ReactionKind>> GetOwnAsync(
        Guid memberId, IReadOnlyCollection<Guid> announcementIds, CancellationToken cancellationToken = default)
    {
        if (announcementIds.Count == 0)
        {
            return new Dictionary<Guid, ReactionKind>();
        }

        return await Reactions.AsNoTracking()
            .Where(r => r.MemberId == memberId && announcementIds.Contains(r.AnnouncementId))
            .ToDictionaryAsync(r => r.AnnouncementId, r => r.Kind, cancellationToken);
    }
}

public sealed class MemberFeedStateRepository(GsmDbContext dbContext) : IMemberFeedStateRepository
{
    private DbSet<MemberFeedState> States => dbContext.Set<MemberFeedState>();

    public Task<MemberFeedState?> GetAsync(Guid memberId, CancellationToken cancellationToken = default)
        => States.FirstOrDefaultAsync(s => s.MemberId == memberId, cancellationToken);

    public void Add(MemberFeedState state) => States.Add(state);
}
