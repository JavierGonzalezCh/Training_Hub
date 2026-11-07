using GSM.Application.Achievements.Abstractions;
using GSM.Domain.Achievements;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Achievements;

public sealed class AchievementRepository(GsmDbContext dbContext) : IAchievementRepository
{
    private DbSet<Achievement> Achievements => dbContext.Set<Achievement>();

    private DbSet<MemberAchievement> Unlocks => dbContext.Set<MemberAchievement>();

    public Task<Achievement?> GetByIdAsync(Guid achievementId, CancellationToken cancellationToken = default)
        => Achievements.FirstOrDefaultAsync(a => a.Id == achievementId, cancellationToken);

    public async Task<IReadOnlyList<Achievement>> ListAsync(
        bool includeInactive, CancellationToken cancellationToken = default)
        => await Achievements
            .Where(a => includeInactive || a.IsActive)
            .OrderBy(a => a.ConditionType).ThenBy(a => a.ConditionValue)
            .ToListAsync(cancellationToken);

    public Task<bool> NameExistsAsync(
        string name, Guid? excludeAchievementId, CancellationToken cancellationToken = default)
        => Achievements.AnyAsync(
            a => a.Name == name && (excludeAchievementId == null || a.Id != excludeAchievementId),
            cancellationToken);

    public void Add(Achievement achievement) => Achievements.Add(achievement);

    public async Task<IReadOnlySet<string>> GetNamesForGymAsync(Guid gymId, CancellationToken cancellationToken = default)
        => (await Achievements.IgnoreQueryFilters()
                .Where(a => a.GymId == gymId)
                .Select(a => a.Name)
                .ToListAsync(cancellationToken))
            .ToHashSet();

    public async Task<IReadOnlyList<Achievement>> ListPendingForMemberAsync(
        Guid memberId, CancellationToken cancellationToken = default)
    {
        var unlockedIds = Unlocks.Where(u => u.MemberId == memberId).Select(u => u.AchievementId);

        return await Achievements
            .Where(a => a.IsActive && a.ConditionType != AchievementConditionType.Manual && !unlockedIds.Contains(a.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, DateTimeOffset>> GetUnlockedForMemberAsync(
        Guid memberId, CancellationToken cancellationToken = default)
        => await Unlocks
            .Where(u => u.MemberId == memberId)
            .ToDictionaryAsync(u => u.AchievementId, u => u.UnlockedAt, cancellationToken);

    public async Task<IReadOnlySet<Guid>> GetMemberIdsWithUnlockAsync(
        Guid achievementId, CancellationToken cancellationToken = default)
        => (await Unlocks.Where(u => u.AchievementId == achievementId).Select(u => u.MemberId)
            .ToListAsync(cancellationToken)).ToHashSet();

    public void AddUnlock(MemberAchievement unlock) => Unlocks.Add(unlock);

    public Task<MemberAchievement?> GetUnlockAsync(
        Guid memberId, Guid achievementId, CancellationToken cancellationToken = default)
        => Unlocks.FirstOrDefaultAsync(
            u => u.MemberId == memberId && u.AchievementId == achievementId, cancellationToken);

    public async Task<RewardOverview> GetRewardOverviewAsync(
        RewardStatus? filter, DateTimeOffset now, int take, CancellationToken cancellationToken = default)
    {
        // Los vencidos son pendientes con el plazo cumplido; los de logros retirados sin entregar no cuentan.
        var rewards = Unlocks.Where(u => u.RewardStatus != RewardStatus.None
            && (u.RewardStatus == RewardStatus.Delivered || u.RevokedAt == null));
        var pendingNow = rewards.Where(u => u.RewardStatus == RewardStatus.Pending
            && (u.RewardExpiresAt == null || u.RewardExpiresAt > now));
        var expired = rewards.Where(u => u.RewardStatus == RewardStatus.Pending
            && u.RewardExpiresAt != null && u.RewardExpiresAt <= now);
        var delivered = rewards.Where(u => u.RewardStatus == RewardStatus.Delivered);

        var pendingCount = await pendingNow.CountAsync(cancellationToken);
        var expiredCount = await expired.CountAsync(cancellationToken);
        var deliveredCount = await delivered.CountAsync(cancellationToken);

        var selected = filter switch
        {
            RewardStatus.Pending => pendingNow,
            RewardStatus.Expired => expired,
            RewardStatus.Delivered => delivered,
            _ => rewards,
        };

        var rows = await (from row in selected
                          join achievement in Achievements on row.AchievementId equals achievement.Id
                          orderby row.UnlockedAt descending
                          select new RewardOverviewRow(row, achievement.Name))
            .Take(take)
            .ToListAsync(cancellationToken);

        return new RewardOverview(pendingCount, deliveredCount, expiredCount, rows);
    }

    private DbSet<StreakFreeze> StreakFreezes => dbContext.Set<StreakFreeze>();

    public async Task<int?> GetBestRankingPositionAsync(Guid memberId, CancellationToken cancellationToken = default)
        => await dbContext.Set<RankingMonthResult>()
            .Where(r => r.MemberId == memberId)
            .MinAsync(r => (int?)r.Position, cancellationToken);

    public Task<bool> IsRankingMonthClosedAsync(Guid gymId, DateOnly month, CancellationToken cancellationToken = default)
        => dbContext.Set<RankingMonthResult>().IgnoreQueryFilters()
            .AnyAsync(r => r.GymId == gymId && r.Month == month, cancellationToken);

    public void AddRankingResult(RankingMonthResult result) => dbContext.Set<RankingMonthResult>().Add(result);

    public async Task<IReadOnlyList<StreakFreeze>> ListStreakFreezesAsync(
        Guid memberId, CancellationToken cancellationToken = default)
        => await StreakFreezes
            .Where(f => f.MemberId == memberId)
            .OrderBy(f => f.GrantedOn)
            .ToListAsync(cancellationToken);

    public void AddStreakFreeze(StreakFreeze freeze) => StreakFreezes.Add(freeze);
}
