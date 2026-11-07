namespace GSM.Domain.Achievements;

/// <summary>
/// Las métricas de los logros de hábitos, disciplina, variedad, fuerza y ranking. Se calculan solo si hay un logro
/// pendiente que las pide. <see cref="BestMonthlyPosition"/>: el mejor puesto en un mes cerrado del ranking (nulo si
/// nunca quedó clasificado).
/// </summary>
public sealed record AchievementExtraMetrics(
    int EarlyVisits = 0,
    int LateVisits = 0,
    int Weekends = 0,
    int CompleteSessions = 0,
    int DistinctClasses = 0,
    int PowerLifts = 0,
    int? BestMonthlyPosition = null)
{
    public static readonly AchievementExtraMetrics None = new();

    /// <summary>Los tipos que se miden con estas métricas.</summary>
    public static bool Uses(AchievementConditionType type) => type is AchievementConditionType.EarlyVisits
        or AchievementConditionType.LateVisits or AchievementConditionType.Weekends
        or AchievementConditionType.CompleteSessions or AchievementConditionType.DistinctClasses
        or AchievementConditionType.PowerLifts or AchievementConditionType.MonthlyRankingTop;
}
