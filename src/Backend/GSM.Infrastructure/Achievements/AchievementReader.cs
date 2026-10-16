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
            .ToDictionaryAsync(u => u.AchievementId, u => new { u.UnlockedAt, u.GrantReason }, cancellationToken);

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
                unlocked.TryGetValue(a.Id, out var reasonRow) ? reasonRow.GrantReason : null))
            .ToList();
    }
}
