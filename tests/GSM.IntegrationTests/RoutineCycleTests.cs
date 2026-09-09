using GSM.Application.Routines;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>Ciclo rotativo: función pura, dos vueltas y fechas anteriores al arranque.</summary>
public sealed class RoutineCycleTests
{
    // Ciclo [D1·D2·D3·descanso] (N=4) arrancando el lunes 2026-03-02.
    private static readonly DateOnly Monday = new(2026, 3, 2);

    [Theory]
    [InlineData(0, 1)] // lunes        -> D1
    [InlineData(1, 2)] // martes       -> D2
    [InlineData(2, 3)] // miércoles    -> D3
    [InlineData(3, 4)] // jueves       -> descanso
    [InlineData(4, 1)] // viernes      -> D1 otra vez
    [InlineData(6, 3)] // domingo (2a vuelta) -> D3, día de entrenamiento: el ciclo se desplaza
    [InlineData(7, 4)] // lunes siguiente     -> descanso
    public void Position_across_two_turns(int offsetDays, int expectedPosition)
        => Assert.Equal(expectedPosition, RoutineCycle.PositionOn(Monday, 4, Monday.AddDays(offsetDays)));

    [Theory]
    [InlineData(-1, 4)]
    [InlineData(-4, 1)]
    [InlineData(-5, 4)]
    public void Dates_before_start_never_go_negative(int offsetDays, int expectedPosition)
        => Assert.Equal(expectedPosition, RoutineCycle.PositionOn(Monday, 4, Monday.AddDays(offsetDays)));
}
