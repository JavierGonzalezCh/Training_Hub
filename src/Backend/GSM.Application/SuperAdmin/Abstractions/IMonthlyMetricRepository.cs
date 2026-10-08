using GSM.Domain.SuperAdmin;

namespace GSM.Application.SuperAdmin.Abstractions;

public interface IMonthlyMetricRepository
{
    void Add(MonthlyMetric metric);

    Task<bool> ExistsAsync(DateOnly month, CancellationToken cancellationToken = default);

    /// <summary>La foto de un mes (solo lectura), o <c>null</c>.</summary>
    Task<MonthlyMetric?> GetAsync(DateOnly month, CancellationToken cancellationToken = default);

    /// <summary>Las últimas <paramref name="count"/> fotos, de la más reciente a la más antigua.</summary>
    Task<IReadOnlyList<MonthlyMetric>> GetLatestAsync(int count, CancellationToken cancellationToken = default);
}
