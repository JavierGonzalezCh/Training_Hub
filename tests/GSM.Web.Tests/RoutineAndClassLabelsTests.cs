using GSM.UI.Shared.Formatting;

namespace GSM.Web.Tests;

public class RoutineAndClassLabelsTests
{
    [Theory]
    [InlineData("Chest", "Pecho")]
    [InlineData("Lower_back", "Lumbar")]
    [InlineData("Full_body", "Cuerpo completo")]
    public void Translates_muscles(string backendName, string expected)
        => Assert.Equal(expected, RoutineLabels.MuscleLabel(backendName));

    [Theory]
    [InlineData("Smith_machine", "Máquina Smith")]
    [InlineData("Bodyweight", "Peso corporal")]
    public void Translates_equipment(string backendName, string expected)
        => Assert.Equal(expected, RoutineLabels.EquipmentLabel(backendName));

    [Fact]
    public void Muscles_and_equipment_catalogs_match_the_closed_backend_enums()
    {
        Assert.Equal(15, RoutineLabels.Muscles.Length);
        Assert.Equal(16, RoutineLabels.Equipment.Length);
    }

    [Theory]
    [InlineData(0, "Lunes")]
    [InlineData(6, "Domingo")]
    public void Translates_days_of_week_by_ordinal(int value, string expected)
        => Assert.Equal(expected, ClassLabels.DayLabel(value));

    [Fact]
    public void Translates_session_and_booking_status()
    {
        Assert.Equal("Cancelada", ClassLabels.SessionStatusLabel("Cancelled"));
        Assert.Equal("No asistió", ClassLabels.BookingStatusLabel("NoShow"));
    }
}
