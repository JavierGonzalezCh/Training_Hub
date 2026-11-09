using GSM.UI.Shared.Formatting;
using Xunit;

namespace GSM.Web.Tests;

/// <summary>E2 — el tipo de negocio se reconoce venga como nombre de C# o como valor de Postgres.</summary>
public sealed class GymTypeLabelsTests
{
    [Theory]
    [InlineData("PersonalTrainer", 3)]
    [InlineData("personal_trainer", 3)]
    [InlineData("personaltrainer", 3)]
    [InlineData("Box", 1)]
    [InlineData("studio", 2)]
    [InlineData(null, 0)]
    [InlineData("desconocido", 0)]
    public void ValueOf_accepts_both_formats(string? type, int expected)
        => Assert.Equal(expected, GymTypeLabels.ValueOf(type));

    [Fact]
    public void Label_reads_the_postgres_format()
        => Assert.Equal("Entrenador personal", GymTypeLabels.Label("personal_trainer"));
}
