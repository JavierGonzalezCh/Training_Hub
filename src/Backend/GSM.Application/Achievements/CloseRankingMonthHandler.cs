using GSM.Application.Achievements.Abstractions;
using GSM.Application.Achievements.Public;
using GSM.Application.Attendance.Public;
using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms.Public;
using GSM.Application.Members.Public;
using GSM.Domain.Achievements;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Achievements;

/// <summary>Cuántos gyms cerraron el mes anterior del ranking en esta ronda y cuántos resultados guardaron.</summary>
public sealed record RankingCloseResult(int GymsClosed, int Results);

/// <summary>
/// Cierre de mes del ranking (logros de ranking, AC1). Lo corre la tarea periódica: para cada gym con el ranking
/// encendido y participantes, guarda una vez el puesto final de cada uno en el mes anterior y vuelve a evaluar sus
/// logros (los de ranking leen el mejor puesto histórico). Idempotente: un mes ya cerrado no se vuelve a cerrar.
/// Corre sin usuario: cada gym se procesa dentro de su <see cref="GymExecutionScope"/>.
/// </summary>
public sealed class CloseRankingMonthHandler(
    IClock clock,
    IGymReader gyms,
    IMemberReader members,
    IAttendanceReader attendance,
    IRankingRepository ranking,
    IAchievementRepository achievements,
    IAchievementService achievementService,
    IUnitOfWork unitOfWork,
    ILogger<CloseRankingMonthHandler> logger)
{
    /// <param name="onlyGymId">Solo para tests: limita el cierre a un gym. En producción es nulo (todos).</param>
    public async Task<RankingCloseResult> HandleAsync(CancellationToken cancellationToken, Guid? onlyGymId = null)
    {
        var today = clock.Today;
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        var month = thisMonth.AddMonths(-1);

        var participantsByGym = (await ranking.ListAsync(cancellationToken))
            .Where(p => onlyGymId is null || p.GymId == onlyGymId)
            .GroupBy(p => p.GymId)
            .ToList();

        var gymsClosed = 0;
        var results = 0;
        foreach (var group in participantsByGym)
        {
            var gymId = group.Key;
            if (await achievements.IsRankingMonthClosedAsync(gymId, month, cancellationToken))
            {
                continue;
            }

            var flags = await gyms.GetFeatureFlagsAsync(gymId, cancellationToken);
            if (flags is null || !flags.ShowRanking || !flags.ShowGamification)
            {
                continue;
            }

            using var scope = GymExecutionScope.Enter(gymId);
            var participants = group.ToList();
            var ids = participants.Select(p => p.MemberId).ToList();
            var views = await members.GetRankingViewsAsync(ids, today, cancellationToken);
            var counts = await attendance.GetDayCountsInRangeAsync(ids, month, thisMonth, cancellationToken);
            var ranked = RankingRules.Rank(participants, views, counts);
            if (ranked.Count == 0)
            {
                continue;
            }

            foreach (var row in ranked)
            {
                achievements.AddRankingResult(
                    RankingMonthResult.Record(gymId, row.Participant.MemberId, month, row.Position, row.Days));
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            gymsClosed++;
            results += ranked.Count;

            foreach (var row in ranked)
            {
                await achievementService.EvaluateForMemberAsync(row.Participant.MemberId, cancellationToken);
            }

            logger.LogInformation(
                "Ranking de {Month} cerrado en el gym {GymId}: {Count} participantes clasificados", month, gymId, ranked.Count);
        }

        return new RankingCloseResult(gymsClosed, results);
    }
}
