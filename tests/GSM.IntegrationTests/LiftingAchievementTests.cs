using GSM.Application.Achievements.Events;
using GSM.Application.Achievements.Public;
using GSM.Application.Routines;
using GSM.Application.Routines.Events;
using GSM.Domain.Achievements;
using GSM.Domain.Routines;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>L3: logros de récords personales (<c>personal_records</c>) y de volumen (<c>lifted_volume</c>).</summary>
public sealed class LiftingAchievementTests
{
    private static readonly Guid Bench = Guid.CreateVersion7();
    private static readonly Guid Squat = Guid.CreateVersion7();

    private static RoutineSetEntry Set(SetType type, int reps, decimal kg) => new(1, type, reps, kg, null, null, null);

    private static (Guid, ProgressLog) Log(Guid exercise, int day, bool self, params RoutineSetEntry[] sets)
        => (exercise, new ProgressLog(new DateOnly(2026, 6, 1).AddDays(day), self, sets));

    [Fact]
    public void The_first_day_is_a_baseline_and_each_later_day_that_beats_the_best_is_one_record()
    {
        var stats = LiftingStatsCalculator.Compute(
        [
            Log(Bench, 0, false, Set(SetType.Normal, 10, 40m)),
            Log(Bench, 7, false, Set(SetType.Normal, 10, 45m)),  // récord
            Log(Bench, 14, false, Set(SetType.Normal, 8, 40m)),  // peor en todo
            Log(Bench, 21, false, Set(SetType.Normal, 5, 60m)),  // récord
        ]);

        Assert.Equal(2, stats.PersonalRecords);
    }

    [Fact]
    public void A_day_that_breaks_several_marks_counts_once_and_each_exercise_has_its_own_baseline()
    {
        var stats = LiftingStatsCalculator.Compute(
        [
            Log(Bench, 0, false, Set(SetType.Normal, 5, 50m)),
            Log(Bench, 7, false, Set(SetType.Normal, 8, 60m)),   // peso, serie y sesión a la vez: un récord
            Log(Squat, 0, false, Set(SetType.Normal, 5, 80m)),   // línea base de otro ejercicio
        ]);

        Assert.Equal(1, stats.PersonalRecords);
    }

    [Fact]
    public void Volume_sums_weight_times_reps_without_warmups_across_exercises_and_days()
    {
        var stats = LiftingStatsCalculator.Compute(
        [
            Log(Bench, 0, false, Set(SetType.Warmup, 12, 20m), Set(SetType.Normal, 10, 40m)), // 400
            Log(Bench, 7, false, Set(SetType.Normal, 5, 50m), Set(SetType.Normal, 5, 50m)),   // 500
            Log(Squat, 0, false, Set(SetType.Normal, 10, 100m)),                              // 1000
        ]);

        Assert.Equal(1900m, stats.LiftedVolumeKg);
    }

    [Fact]
    public void Nothing_logged_gives_zero()
    {
        var stats = LiftingStatsCalculator.Compute([]);
        Assert.Equal((0, 0m), (stats.PersonalRecords, stats.LiftedVolumeKg));
    }

    [Theory]
    [InlineData(AchievementConditionType.PersonalRecords, 3, 0, 3, true)]
    [InlineData(AchievementConditionType.PersonalRecords, 3, 0, 2, false)]
    [InlineData(AchievementConditionType.LiftedVolume, 10000, 10000, 0, true)]
    [InlineData(AchievementConditionType.LiftedVolume, 10000, 9999, 50, false)]
    public void The_condition_reads_the_matching_metric(
        AchievementConditionType type, int value, int volumeKg, int records, bool met)
    {
        var achievement = Achievement.Create(Guid.CreateVersion7(), "Prueba", null, null, type, value);

        Assert.Equal(met, achievement.IsMetBy(0, 0, 0, 0, personalRecords: records, liftedVolumeKg: volumeKg));
        Assert.Equal(
            Math.Min(type == AchievementConditionType.PersonalRecords ? records : volumeKg, value),
            achievement.ProgressOf(0, 0, 0, 0, personalRecords: records, liftedVolumeKg: volumeKg));
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public async Task The_handler_ignores_self_assigned_records_and_evaluates_the_rest(bool selfAssigned, int expectedCalls)
    {
        var service = new CountingService();
        var handler = new PersonalRecordSetHandler(service, NullLogger<PersonalRecordSetHandler>.Instance);

        await handler.HandleAsync(new PersonalRecordSet(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Bench, new DateOnly(2026, 6, 8), selfAssigned,
            ["best_weight"]));

        Assert.Equal(expectedCalls, service.Calls);
    }

    private sealed class CountingService : IAchievementService
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<UnlockedAchievement>> EvaluateForMemberAsync(
            Guid memberId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<UnlockedAchievement>>([]);
        }

        public Task<StreakFreezeState> GetOrGrantStreakFreezesAsync(
            Guid memberId, DateOnly today, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> ConsumeStreakFreezesAsync(
            Guid memberId, IReadOnlyCollection<DateOnly> failedDates, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
