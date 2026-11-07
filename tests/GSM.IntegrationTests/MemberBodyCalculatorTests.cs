using GSM.Application.Routines;
using GSM.Domain.Routines;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>Recuperación muscular estimada y volumen semanal: función pura, sin base de datos.</summary>
public sealed class MemberBodyCalculatorTests
{
    private static readonly DateOnly Friday = new(2026, 11, 6);

    private static RoutineSetEntry Set(int n, decimal kg, int reps, SetType type = SetType.Normal)
        => new(n, type, reps, kg, null, null, null);

    private static BodyLog Log(DateOnly day, Muscle primary, Muscle[] secondary, int sets, decimal kg = 50, int reps = 10)
        => new(day, primary, secondary, Enumerable.Range(1, sets).Select(n => Set(n, kg, reps)).ToList());

    private static int Percent(MyBodyStatusResponse status, Muscle muscle)
        => status.Muscles.Single(m => m.Muscle == muscle.ToString()).RecoveryPercent;

    [Fact]
    public void Training_today_leaves_the_primary_at_zero_and_the_secondary_at_half()
    {
        var status = MemberBodyCalculator.Status([Log(Friday, Muscle.Chest, [Muscle.Triceps], 4)], Friday);

        Assert.Equal(0, Percent(status, Muscle.Chest));
        Assert.Equal(50, Percent(status, Muscle.Triceps));
        Assert.Equal(100, Percent(status, Muscle.Quadriceps));
        Assert.Equal(0, status.DaysSinceLastWorkout);
        Assert.Equal(MemberBodyCalculator.MapMuscles.Count - 2, status.ReadyCount);
    }

    [Fact]
    public void Recovery_grows_with_the_days_and_more_sets_take_longer()
    {
        var yesterday = Friday.AddDays(-1);
        var light = MemberBodyCalculator.Status([Log(yesterday, Muscle.Quadriceps, [], 4)], Friday);
        var heavy = MemberBodyCalculator.Status([Log(yesterday, Muscle.Quadriceps, [], 8)], Friday);
        var later = MemberBodyCalculator.Status([Log(Friday.AddDays(-2), Muscle.Quadriceps, [], 4)], Friday);

        Assert.Equal(50, Percent(light, Muscle.Quadriceps));
        Assert.Equal(33, Percent(heavy, Muscle.Quadriceps));
        Assert.Equal(100, Percent(later, Muscle.Quadriceps));
        Assert.Equal(1, light.DaysSinceLastWorkout);
    }

    [Fact]
    public void The_most_tiring_log_wins_and_warmups_do_not_count()
    {
        var logs = new[]
        {
            Log(Friday.AddDays(-1), Muscle.Back, [], 3),
            new BodyLog(Friday, Muscle.Back, [], [Set(1, 20, 10, SetType.Warmup)]),
            Log(Friday, Muscle.Biceps, [Muscle.Back], 3),
        };

        var status = MemberBodyCalculator.Status(logs, Friday);

        Assert.Equal(50, Percent(status, Muscle.Back));
        Assert.Equal(Friday, status.Muscles.Single(m => m.Muscle == "Back").LastTrainedOn);
    }

    [Fact]
    public void Without_logs_everything_is_ready_and_there_is_no_last_workout()
    {
        var status = MemberBodyCalculator.Status([], Friday);

        Assert.Null(status.DaysSinceLastWorkout);
        Assert.All(status.Muscles, m => Assert.Equal(100, m.RecoveryPercent));
        Assert.DoesNotContain(status.Muscles, m => m.Muscle == "Full_body");
    }

    [Fact]
    public void Weekly_volume_sums_weight_by_reps_per_week_with_empty_weeks_in_between()
    {
        var logs = new[]
        {
            Log(Friday, Muscle.Chest, [], 3, kg: 50, reps: 10),                 // esta semana: 1500
            Log(Friday.AddDays(-1), Muscle.Back, [], 2, kg: 40, reps: 8),       // esta semana: 640
            new BodyLog(Friday, Muscle.Chest, [], [Set(1, 20, 10, SetType.Warmup)]),
            Log(Friday.AddDays(-14), Muscle.Quadriceps, [], 4, kg: 100, reps: 5), // hace dos semanas: 2000
        };

        var weeks = MemberBodyCalculator.WeeklyVolume(logs, Friday, 3);

        Assert.Equal(3, weeks.Count);
        Assert.Equal(new DateOnly(2026, 11, 2), weeks[^1].WeekStart);
        Assert.Equal(2140m, weeks[^1].VolumeKg);
        Assert.Equal(5, weeks[^1].Sets);
        Assert.Equal(2, weeks[^1].Sessions);
        Assert.Equal(0m, weeks[1].VolumeKg);
        Assert.Equal(2000m, weeks[0].VolumeKg);
    }
}
