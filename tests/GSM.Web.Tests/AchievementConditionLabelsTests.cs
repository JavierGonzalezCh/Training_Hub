using GSM.UI.Shared.Formatting;

namespace GSM.Web.Tests;

public class AchievementConditionLabelsTests
{
    [Theory]
    [InlineData("Visits", 0, "Visitas")]
    [InlineData("Streak", 1, "Racha (días seguidos)")]
    [InlineData("RoutineSessions", 2, "Días con rutina registrada")]
    [InlineData("Classes", 3, "Clases")]
    [InlineData("TenureMonths", 4, "Antigüedad (meses)")]
    [InlineData("Referrals", 5, "Amigos referidos")]
    [InlineData("Manual", 6, "Otorgado por el staff")]
    [InlineData("PersonalRecords", 7, "Récords personales")]
    [InlineData("LiftedVolume", 8, "Volumen levantado (kg)")]
    [InlineData("EarlyVisits", 9, "Entradas antes de las 8:00")]
    [InlineData("LateVisits", 10, "Entradas desde las 20:00")]
    [InlineData("Weekends", 11, "Fines de semana")]
    [InlineData("CompleteSessions", 12, "Sesiones de rutina completas")]
    [InlineData("DistinctClasses", 13, "Clases distintas")]
    [InlineData("PowerLifts", 14, "Sentadilla, banca y peso muerto")]
    [InlineData("MonthlyRankingTop", 15, "Puesto en el ranking del mes")]
    public void Round_trips_between_the_backend_name_and_the_select_value(string backendName, int value, string label)
    {
        Assert.Equal(value, AchievementConditionLabels.ToValue(backendName));
        Assert.Contains((value, label), AchievementConditionLabels.All);
        Assert.False(string.IsNullOrWhiteSpace(AchievementConditionLabels.Label(backendName)));
        Assert.NotEqual(backendName, AchievementConditionLabels.Label(backendName));
    }

    [Fact]
    public void Every_condition_appears_once_in_the_editor()
        => Assert.Equal(16, AchievementConditionLabels.All.Select(a => a.Value).Distinct().Count());

    [Theory]
    [InlineData(4, "meses")]
    [InlineData(5, "amigos")]
    [InlineData(7, "récords")]
    [InlineData(8, "kilos")]
    [InlineData(9, "8:00")]
    [InlineData(15, "puesto")]
    public void The_value_label_says_what_the_number_counts(int value, string expected)
        => Assert.Contains(expected, AchievementConditionLabels.ValueLabel(value));

    [Theory]
    [InlineData("Visits", 7, 10, "7/10")]
    [InlineData("MonthlyRankingTop", 0, 10, "Top 10 en un mes")]
    [InlineData("MonthlyRankingTop", 0, 1, "Primer puesto en un mes")]
    public void Progress_reads_as_a_count_or_as_a_ranking_goal(string type, int progress, int value, string expected)
        => Assert.Equal(expected, AchievementConditionLabels.Progress(type, progress, value));
}
