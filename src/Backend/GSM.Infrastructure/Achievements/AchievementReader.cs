using GSM.Application.Achievements.Public;
using GSM.Application.Attendance.Public;
using GSM.Application.Classes.Public;
using GSM.Application.Common.Abstractions;
using GSM.Application.Members.Public;
using GSM.Application.Memberships.Public;
using GSM.Application.Routines.Public;
using GSM.Domain.Achievements;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Achievements;

public sealed class AchievementReader(
    GsmDbContext dbContext,
    IClock clock,
    IAttendanceReader attendance,
    IRoutineReader routines,
    IClassReader classes,
    IMemberReader members,
    IMembershipReader memberships) : IAchievementReader
{
    public Task<bool> HasAchievementsAsync(Guid gymId, CancellationToken cancellationToken = default)
        => dbContext.Set<Achievement>().IgnoreQueryFilters().AnyAsync(a => a.GymId == gymId, cancellationToken);

    public Task<string?> GetAchievementNameAsync(Guid achievementId, CancellationToken cancellationToken = default)
        => dbContext.Set<Achievement>().Where(a => a.Id == achievementId).Select(a => (string?)a.Name)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<StreakFreezeView>> GetStreakFreezesAsync(
        Guid memberId, CancellationToken cancellationToken = default)
        => await dbContext.Set<StreakFreeze>()
            .Where(f => f.MemberId == memberId)
            .OrderBy(f => f.GrantedOn)
            .Select(f => new StreakFreezeView(f.GrantedOn, f.UsedOn))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MemberAchievementView>> GetForMemberAsync(
        Guid memberId, Guid gymId, CancellationToken cancellationToken = default)
    {
        var unlocked = await dbContext.Set<MemberAchievement>()
            .Where(u => u.MemberId == memberId && u.RevokedAt == null)
            .ToDictionaryAsync(u => u.AchievementId, cancellationToken);

        // L5-3: un logro desactivado deja de verse, salvo el que el miembro ganó y aún tiene un premio por reclamar.
        var withPendingReward = unlocked.Values
            .Where(u => u.RewardStatus == RewardStatus.Pending)
            .Select(u => u.AchievementId)
            .ToList();

        var achievements = await dbContext.Set<Achievement>()
            .Where(a => a.GymId == gymId && (a.IsActive || withPendingReward.Contains(a.Id)))
            .OrderBy(a => a.ConditionType).ThenBy(a => a.ConditionValue)
            .ToListAsync(cancellationToken);
        var now = clock.UtcNow;

        var (visits, streak, sessions, classAttendance, tenure, referrals, records, volume) = await AchievementMetrics.ComputeAsync(
            memberId, clock.Today, attendance, routines, classes, members, memberships,
            needTenure: achievements.Any(a => a.ConditionType == AchievementConditionType.TenureMonths),
            needReferrals: achievements.Any(a => a.ConditionType == AchievementConditionType.Referrals),
            needLifting: achievements.Any(a => a.ConditionType is AchievementConditionType.PersonalRecords or AchievementConditionType.LiftedVolume),
            cancellationToken);

        return achievements
            .Select(a => new MemberAchievementView(
                a.Id, a.Name, a.Description, a.IconUrl, a.ConditionType, a.ConditionValue,
                unlocked.ContainsKey(a.Id),
                unlocked.TryGetValue(a.Id, out var row) ? row.UnlockedAt : null,
                // Un logro ganado se muestra cumplido: la métrica viva (p. ej. la racha de hoy) puede haber bajado después.
                a.IsManual ? (unlocked.ContainsKey(a.Id) ? 1 : 0)
                    : unlocked.ContainsKey(a.Id) ? a.ConditionValue
                    : a.ProgressOf(visits, streak, sessions, classAttendance, tenure, referrals, records, volume),
                a.IsManual,
                unlocked.TryGetValue(a.Id, out var reasonRow) ? reasonRow.GrantReason : null,
                // Ganado: el premio prometido al ganarlo. Sin ganar: el que ofrece el logro hoy.
                unlocked.TryGetValue(a.Id, out var rewardRow) ? rewardRow.RewardDescription : a.RewardDescription,
                unlocked.TryGetValue(a.Id, out var statusRow) ? statusRow.EffectiveRewardStatus(now) : RewardStatus.None,
                unlocked.TryGetValue(a.Id, out var expiryRow) ? expiryRow.RewardExpiresAt : null,
                unlocked.TryGetValue(a.Id, out var deliveredRow) ? deliveredRow.RewardDeliveredAt : null))
            .ToList();
    }

    public async Task<IReadOnlyList<PendingRewardView>> GetPendingRewardsAsync(
        Guid memberId, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var rows = await RewardQuery()
            .Where(x => x.Row.MemberId == memberId
                && x.Row.RewardStatus == RewardStatus.Pending
                && x.Row.RevokedAt == null
                && (x.Row.RewardExpiresAt == null || x.Row.RewardExpiresAt > now))
            .OrderBy(x => x.Row.RewardExpiresAt == null).ThenBy(x => x.Row.RewardExpiresAt).ThenBy(x => x.Row.UnlockedAt)
            .ToListAsync(cancellationToken);
        return rows.Select(ToView).ToList();
    }

    public async Task<PendingRewardView?> GetRewardAsync(
        Guid memberAchievementId, CancellationToken cancellationToken = default)
    {
        var x = await RewardQuery()
            .Where(r => r.Row.Id == memberAchievementId && r.Row.RewardStatus == RewardStatus.Pending)
            .FirstOrDefaultAsync(cancellationToken);
        return x is null ? null : ToView(x);
    }

    private IQueryable<RewardRow> RewardQuery()
        => from row in dbContext.Set<MemberAchievement>()
           join achievement in dbContext.Set<Achievement>() on row.AchievementId equals achievement.Id
           select new RewardRow { Row = row, AchievementName = achievement.Name };

    private static PendingRewardView ToView(RewardRow x) => new(
        x.Row.Id, x.Row.MemberId, x.Row.GymId, x.Row.AchievementId, x.AchievementName,
        x.Row.RewardDescription ?? string.Empty, x.Row.RewardExpiresAt);

    // Inicializador de objeto, no constructor: EF no traduce filtros sobre un record posicional.
    private sealed class RewardRow
    {
        public required MemberAchievement Row { get; init; }
        public required string AchievementName { get; init; }
    }
}
