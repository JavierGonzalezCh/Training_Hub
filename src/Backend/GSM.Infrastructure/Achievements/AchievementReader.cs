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
    public Task<string?> GetAchievementNameAsync(Guid achievementId, CancellationToken cancellationToken = default)
        => dbContext.Set<Achievement>().Where(a => a.Id == achievementId).Select(a => (string?)a.Name)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<MemberAchievementView>> GetForMemberAsync(
        Guid memberId, Guid gymId, CancellationToken cancellationToken = default)
    {
        var achievements = await dbContext.Set<Achievement>()
            .Where(a => a.GymId == gymId && a.IsActive)
            .OrderBy(a => a.ConditionType).ThenBy(a => a.ConditionValue)
            .ToListAsync(cancellationToken);

        var unlocked = await dbContext.Set<MemberAchievement>()
            .Where(u => u.MemberId == memberId && u.RevokedAt == null)
            .ToDictionaryAsync(u => u.AchievementId, cancellationToken);
        var now = clock.UtcNow;

        var (visits, streak, sessions, classAttendance, tenure, referrals) = await AchievementMetrics.ComputeAsync(
            memberId, clock.Today, attendance, routines, classes, members, memberships,
            needTenure: achievements.Any(a => a.ConditionType == AchievementConditionType.TenureMonths),
            needReferrals: achievements.Any(a => a.ConditionType == AchievementConditionType.Referrals),
            cancellationToken);

        return achievements
            .Select(a => new MemberAchievementView(
                a.Id, a.Name, a.Description, a.IconUrl, a.ConditionType, a.ConditionValue,
                unlocked.ContainsKey(a.Id),
                unlocked.TryGetValue(a.Id, out var row) ? row.UnlockedAt : null,
                a.IsManual ? (unlocked.ContainsKey(a.Id) ? 1 : 0)
                    : a.ProgressOf(visits, streak, sessions, classAttendance, tenure, referrals),
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
