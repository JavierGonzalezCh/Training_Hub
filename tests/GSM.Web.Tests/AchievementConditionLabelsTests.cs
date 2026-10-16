using GSM.UI.Shared.Formatting;

namespace GSM.Web.Tests;

public class AchievementConditionLabelsTests
{
    [Theory]
    [InlineData("Visits", 0, "Visitas")]
    [InlineData("Streak", 1, "Racha (días seguidos)")]
    [InlineData("RoutineSessions", 2, "Sesiones de rutina")]
    [InlineData("Classes", 3, "Clases")]
    [InlineData("TenureMonths", 4, "Antigüedad (meses)")]
    [InlineData("Referrals", 5, "Amigos referidos")]
    [InlineData("Manual", 6, "Otorgado por el staff")]
    public void Round_trips_between_the_backend_name_and_the_select_value(string backendName, int value, string label)
    {
        Assert.Equal(value, AchievementConditionLabels.ToValue(backendName));
        Assert.Equal(label, AchievementConditionLabels.Label(backendName));
        Assert.Equal((value, label), AchievementConditionLabels.All[value]);
    }

    [Theory]
    [InlineData(4, "meses")]
    [InlineData(5, "amigos")]
    public void The_value_label_says_what_the_number_counts(int value, string expected)
        => Assert.Contains(expected, AchievementConditionLabels.ValueLabel(value));
}
