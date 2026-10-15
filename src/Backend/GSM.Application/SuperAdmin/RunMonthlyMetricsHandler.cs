using GSM.Application.Attendance.Public;
using GSM.Application.Common.Abstractions;
using GSM.Application.Memberships.Public;
using GSM.Application.SuperAdmin.Abstractions;
using GSM.Domain.SuperAdmin;
using Microsoft.Extensions.Logging;

namespace GSM.Application.SuperAdmin;

/// <summary>
/// Cierra el mes anterior: guarda su foto en <c>superadmin.monthly_metrics</c> la primera vez que corre la
/// tarea periódica del mes nuevo. Idempotente por mes (si la fila existe, no hace nada) y sin relleno hacia
/// atrás: solo cierra el mes inmediatamente anterior. Corre sin usuario, así que ve todos los gyms.
/// </summary>
public sealed class RunMonthlyMetricsHandler(
    IClock clock,
    IGymSubscriptionRepository subscriptions,
    ISubscriptionPaymentRepository payments,
    IMonthlyMetricRepository metrics,
    IMembershipReader memberships,
    IAttendanceReader attendance,
    IUnitOfWork unitOfWork,
    ILogger<RunMonthlyMetricsHandler> logger)
{
    /// <returns>1 si creó la foto del mes anterior, 0 si ya existía.</returns>
    public async Task<int> HandleAsync(CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var month = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);

        if (await metrics.ExistsAsync(month, cancellationToken))
        {
            return 0;
        }

        var next = month.AddMonths(1);
        var lastDay = next.AddDays(-1);

        var rows = await subscriptions.GetPortfolioAsync(cancellationToken);
        var inputs = rows
            .Select(r => new GymMetricInput(r.GymId, r.ManualStatus, r.MonthlyPrice, r.StartedOn, r.TrialEndsOn, r.PaidThrough, r.TrialGraceDays))
            .ToList();

        var previous = await metrics.GetAsync(month.AddMonths(-1), cancellationToken);
        var counts = MonthlyMetricsCalculator.Compute(inputs, month, previous?.GymStates);

        var collected = await payments.SumInRangeAsync(
            clock.BusinessDayRange(month).Start, clock.BusinessDayRange(next).Start, cancellationToken);
        var activeMembers = await memberships.GetCoveredMemberCountAsync(lastDay, cancellationToken);
        var checkIns = (await attendance.GetMonthlyCountsByGymAsync(month.Year, month.Month, cancellationToken))
            .Values.Sum();

        metrics.Add(MonthlyMetric.Create(
            month, counts.Mrr, collected, counts.NewGyms, counts.TrialGymsAtClose, counts.ConvertedGyms,
            counts.LostGyms, counts.TrialsNotConverted, counts.ActiveGymsAtStart, counts.ActiveGymsAtClose, activeMembers, checkIns,
            counts.StatesAtClose));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Métricas mensuales de {Month:yyyy-MM} guardadas", month);
        return 1;
    }
}
