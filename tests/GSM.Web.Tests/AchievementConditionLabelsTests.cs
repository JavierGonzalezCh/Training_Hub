using GSM.UI.Shared.Formatting;

namespace GSM.Web.Tests;

public class AchievementConditionLabelsTests
{
    [Theory]
    [InlineData("Visits", 0, "Visitas")]
    [InlineData("Streak", 1, "Racha (días seguidos)")]
    [InlineData("RoutineSessions", 2, "Sesiones de rutina")]
    [InlineData("Classes", 3, "Clases")]
    public void Round_trips_between_the_backend_name_and_the_select_value(string backendName, int value, string label)
    {
        Assert.Equal(value, AchievementConditionLabels.ToValue(backendName));
        Assert.Equal(label, AchievementConditionLabels.Label(backendName));
        Assert.Equal((value, label), AchievementConditionLabels.All[value]);
    }
}
