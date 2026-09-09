using GSM.Application.Achievements.Public;
using GSM.Application.Attendance.Public;
using GSM.Application.Classes.Public;
using GSM.Application.Common.Abstractions;
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
    IClassReader classes) : IAchievementReader
{
    public async Task<IReadOnlyList<MemberAchievementView>> GetForMemberAsync(
        Guid memberId, Guid gymId, CancellationToken cancellationToken = default)
    {
        var achievements = await dbContext.Set<Achievement>()
            .Where(a => a.GymId == gymId && a.IsActive)
            .OrderBy(a => a.ConditionType).ThenBy(a => a.ConditionValue)
            .ToListAsync(cancellationToken);

        var unlocked = await dbContext.Set<MemberAchievement>()
            .Where(u => u.MemberId == memberId)
            .ToDictionaryAsync(u => u.AchievementId, u => u.UnlockedAt, cancellationToken);

        var (visits, streak, sessions, classAttendance) = await AchievementMetrics.ComputeAsync(
            memberId, clock.Today, attendance, routines, classes, cancellationToken);

        return achievements
            .Select(a => new MemberAchievementView(
                a.Id, a.Name, a.Description, a.IconUrl, a.ConditionType, a.ConditionValue,
                unlocked.ContainsKey(a.Id),
                unlocked.TryGetValue(a.Id, out var at) ? at : null,
                a.ProgressOf(visits, streak, sessions, classAttendance)))
            .ToList();
    }
}
