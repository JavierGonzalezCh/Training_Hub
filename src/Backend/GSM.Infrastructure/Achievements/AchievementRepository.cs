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

    private DbSet<StreakFreeze> StreakFreezes => dbContext.Set<StreakFreeze>();

    public async Task<IReadOnlyList<StreakFreeze>> ListStreakFreezesAsync(
        Guid memberId, CancellationToken cancellationToken = default)
        => await StreakFreezes
            .Where(f => f.MemberId == memberId)
            .OrderBy(f => f.GrantedOn)
            .ToListAsync(cancellationToken);

    public void AddStreakFreeze(StreakFreeze freeze) => StreakFreezes.Add(freeze);
}
