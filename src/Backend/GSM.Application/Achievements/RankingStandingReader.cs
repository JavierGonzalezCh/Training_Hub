using GSM.Application.Achievements.Abstractions;
using GSM.Application.Achievements.Public;
using GSM.Application.Attendance.Public;
using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms.Public;
using GSM.Application.Members.Public;

namespace GSM.Application.Achievements;

/// <summary>
/// Posición del miembro en el ranking del mes (D2). Mismo cálculo que <see cref="GetMyRankingHandler"/> (comparte
/// <see cref="RankingRules"/>), sin armar el top ni el podio.
/// </summary>
public sealed class RankingStandingReader(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    IMemberReader members,
    IAttendanceReader attendance,
    IRankingRepository ranking) : IRankingStandingReader
{
    public async Task<RankingStandingView?> GetAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        var gymId = currentUser.RequireGymId();
        var flags = await gyms.GetFeatureFlagsAsync(gymId, cancellationToken);
        if (flags is null || !flags.ShowRanking || !flags.ShowGamification)
        {
            return null;
        }

        var participants = await ranking.ListAsync(cancellationToken);
        if (participants.All(p => p.MemberId != memberId))
        {
            return new RankingStandingView(false, null, 0);
        }

        var today = clock.Today;
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        var ids = participants.Select(p => p.MemberId).Distinct().ToList();
        var views = await members.GetRankingViewsAsync(ids, today, cancellationToken);
        var counts = await attendance.GetDayCountsInRangeAsync(ids, thisMonth, thisMonth.AddMonths(1), cancellationToken);

        var mine = RankingRules.Rank(participants, views, counts).FirstOrDefault(r => r.Participant.MemberId == memberId);
        return new RankingStandingView(
            true,
            mine?.Position,
            counts.FirstOrDefault(c => c.MemberId == memberId)?.Days ?? 0);
    }
}
