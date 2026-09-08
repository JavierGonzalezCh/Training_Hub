using GSM.Application.Attendance;
using Xunit;

namespace GSM.IntegrationTests;

public sealed class StreakCalculatorTests
{
    private static DateOnly D(int day) => new(2026, 3, day);

    [Fact]
    public void Empty_history_is_zero()
        => Assert.Equal(0, StreakCalculator.Compute([], D(10)));

    [Fact]
    public void Consecutive_days_count_fully()
        => Assert.Equal(4, StreakCalculator.Compute([D(7), D(8), D(9), D(10)], D(10)));

    [Fact]
    public void One_rest_day_between_visits_keeps_the_run()
        // 1,2 (gap 0), 4 (gap 1), 5 (gap 0) → corrida de 4; hoy = 6 (1 día de descanso)
        => Assert.Equal(4, StreakCalculator.Compute([D(1), D(2), D(4), D(5)], D(6)));

    [Fact]
    public void Two_rest_days_break_the_run()
        // 5 → 8 hay 2 días sin asistir → nueva corrida; solo cuenta el 8
        => Assert.Equal(1, StreakCalculator.Compute([D(1), D(2), D(4), D(5), D(8)], D(8)));

    [Fact]
    public void Streak_is_dead_if_last_visit_is_more_than_two_days_ago()
        => Assert.Equal(0, StreakCalculator.Compute([D(1), D(2), D(4), D(5)], D(8)));

    [Fact]
    public void Streak_alive_at_exactly_two_days_gap()
        => Assert.Equal(4, StreakCalculator.Compute([D(1), D(2), D(4), D(5)], D(7)));
}
