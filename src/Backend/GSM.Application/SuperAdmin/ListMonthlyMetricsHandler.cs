using GSM.Application.SuperAdmin.Abstractions;
using GSM.Application.SuperAdmin.Contracts;

namespace GSM.Application.SuperAdmin;

/// <summary>Evolución de las métricas del SaaS: hasta los últimos 12 meses guardados, del más antiguo al más reciente.</summary>
public sealed class ListMonthlyMetricsHandler(IMonthlyMetricRepository metrics)
{
    public const int MaxMonths = 12;

    public async Task<MonthlyMetricsResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var rows = await metrics.GetLatestAsync(MaxMonths, cancellationToken);
        return new MonthlyMetricsResponse(rows
            .OrderBy(m => m.Month)
            .Select(m => new MonthlyMetricItem(
                m.Month, m.Mrr, m.Collected, m.NewGyms, m.TrialGymsAtClose, m.ConvertedGyms, m.LostGyms, m.TrialsNotConverted,
                m.ActiveGymsAtStart, m.ActiveGymsAtClose, m.Churn, m.ActiveMembers, m.CheckIns))
            .ToList());
    }
}
