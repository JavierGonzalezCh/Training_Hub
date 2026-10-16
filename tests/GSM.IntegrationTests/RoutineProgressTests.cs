using GSM.Application.Routines;
using GSM.Application.Routines.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Routines;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>Progreso del miembro y récords personales (R7).</summary>
public sealed class RoutineProgressTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // ---- Cálculo puro --------------------------------------------------------------------------------------

    [Fact]
    public void Warmup_sets_do_not_count_and_a_day_gets_one_point_with_the_best_of_its_sets()
    {
        var day = new DateOnly(2026, 6, 1);
        var point = MemberProgressCalculator.PointOf(day,
        [
            Set(SetType.Warmup, 12, 20m),
            Set(SetType.Normal, 10, 40m),
            Set(SetType.Failure, 6, 50m),
        ]);

        Assert.NotNull(point);
        Assert.Equal(2, point!.SetCount);
        Assert.Equal(50m, point.BestWeightKg);
        Assert.Equal(400m, point.BestSetVolume); // 10 × 40
        Assert.Equal(700m, point.TotalVolume);   // 400 + 300
        Assert.Equal(16, point.TotalReps);
    }

    [Fact]
    public void A_day_with_only_warmups_or_no_values_has_no_point()
    {
        var day = new DateOnly(2026, 6, 1);
        Assert.Null(MemberProgressCalculator.PointOf(day, [Set(SetType.Warmup, 12, 20m)]));
        Assert.Null(MemberProgressCalculator.PointOf(day, [Set(SetType.Normal, null, null)]));
    }

    [Fact]
    public void Records_depend_on_what_the_exercise_measures_and_remember_the_first_day_they_were_reached()
    {
        var points = MemberProgressCalculator.Points(
        [
            new ProgressLog(new DateOnly(2026, 6, 1), false, [Set(SetType.Normal, 10, 40m)]),
            new ProgressLog(new DateOnly(2026, 6, 8), false, [Set(SetType.Normal, 8, 50m)]),
            new ProgressLog(new DateOnly(2026, 6, 15), false, [Set(SetType.Normal, 8, 50m)]), // empata: gana el primero
        ]);

        var records = MemberProgressCalculator.Records(TrackingType.RepsWeight, points);

        var weight = Assert.Single(records, r => r.Kind == RecordKinds.BestWeight);
        Assert.Equal((50m, new DateOnly(2026, 6, 8)), (weight.Value, weight.AchievedOn));
        Assert.Equal(400m, Assert.Single(records, r => r.Kind == RecordKinds.BestSetVolume).Value);
        Assert.DoesNotContain(records, r => r.Kind == RecordKinds.BestDistance);

        var timeOnly = MemberProgressCalculator.Records(
            TrackingType.Time,
            MemberProgressCalculator.Points([new ProgressLog(new DateOnly(2026, 6, 1), false, [new RoutineSetEntry(1, SetType.Normal, null, null, 90, null, null)])]));
        var duration = Assert.Single(timeOnly);
        Assert.Equal((RecordKinds.BestDuration, 90m, "s"), (duration.Kind, duration.Value, duration.Unit));
    }

    [Fact]
    public void The_first_ever_mark_is_a_baseline_not_a_record()
    {
        var today = MemberProgressCalculator.PointOf(new DateOnly(2026, 6, 1), [Set(SetType.Normal, 10, 40m)]);
        Assert.Empty(MemberProgressCalculator.BrokenKinds([], null, today));
    }

    // ---- Con base de datos ---------------------------------------------------------------------------------

    [Fact]
    public async Task Beating_the_best_weight_publishes_one_record_event_and_saving_again_does_not_repeat_it()
    {
        var w = await SeedAsync();
        factory.Services.GetRequiredService<RoutineEventRecorder>().Clear();

        await LogAsync(w, new DateOnly(2026, 6, 1), w.LoadLine, 80m, 5);
        Assert.Empty(factory.Services.GetRequiredService<RoutineEventRecorder>().Records); // línea base

        await LogAsync(w, new DateOnly(2026, 6, 8), w.LoadLine, 85m, 5);
        var record = Assert.Single(factory.Services.GetRequiredService<RoutineEventRecorder>().Records);
        Assert.Equal((w.MemberId, w.LoadExerciseId, new DateOnly(2026, 6, 8)), (record.MemberId, record.ExerciseId, record.PerformedOn));
        Assert.False(record.SelfAssigned);
        Assert.Contains(RecordKinds.BestWeight, record.Kinds);

        // Guardar lo mismo otra vez no es otro récord; superarse a sí mismo el mismo día sí.
        await LogAsync(w, new DateOnly(2026, 6, 8), w.LoadLine, 85m, 5);
        Assert.Single(factory.Services.GetRequiredService<RoutineEventRecorder>().Records);

        await LogAsync(w, new DateOnly(2026, 6, 8), w.LoadLine, 90m, 5);
        Assert.Equal(2, factory.Services.GetRequiredService<RoutineEventRecorder>().Records.Count);
    }

    [Fact]
    public async Task Doing_worse_than_the_best_publishes_no_record()
    {
        var w = await SeedAsync();
        factory.Services.GetRequiredService<RoutineEventRecorder>().Clear();

        await LogAsync(w, new DateOnly(2026, 6, 1), w.LoadLine, 80m, 5);
        await LogAsync(w, new DateOnly(2026, 6, 8), w.LoadLine, 70m, 5);

        Assert.Empty(factory.Services.GetRequiredService<RoutineEventRecorder>().Records);
    }

    [Fact]
    public async Task Progress_lists_exercises_with_records_and_the_detail_gives_one_point_per_day()
    {
        var w = await SeedAsync();
        await LogAsync(w, new DateOnly(2026, 6, 1), w.LoadLine, 80m, 5);
        await LogAsync(w, new DateOnly(2026, 6, 8), w.LoadLine, 85m, 5);
        await LogAsync(w, new DateOnly(2026, 6, 8), w.TimeLine, null, null, durationSeconds: 60);

        using var scope = AsMember(w, new DateOnly(2026, 6, 9));
        var list = await scope.ServiceProvider.GetRequiredService<GetMyProgressHandler>().HandleAsync(default);

        Assert.Equal(2, list.Count);
        var load = Assert.Single(list, s => s.ExerciseId == w.LoadExerciseId);
        Assert.Equal(2, load.Sessions);
        Assert.Equal(85m, Assert.Single(load.Records, r => r.Kind == RecordKinds.BestWeight).Value);
        var timed = Assert.Single(list, s => s.ExerciseId == w.TimeExerciseId);
        Assert.Equal(60m, Assert.Single(timed.Records, r => r.Kind == RecordKinds.BestDuration).Value);

        var detail = await scope.ServiceProvider.GetRequiredService<GetMyExerciseProgressHandler>()
            .HandleAsync(w.LoadExerciseId, default);
        Assert.Equal([new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 8)], detail.Points.Select(p => p.PerformedOn));
        Assert.Equal([80m, 85m], detail.Points.Select(p => p.BestWeightKg));
    }

    [Fact]
    public async Task Staff_sees_the_progress_of_a_member_and_an_unknown_member_is_404()
    {
        var w = await SeedAsync();
        await LogAsync(w, new DateOnly(2026, 6, 1), w.LoadLine, 80m, 5);

        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(w.StaffUserId, w.GymId, UserRole.Staff);

        var list = await scope.ServiceProvider.GetRequiredService<GetMemberProgressHandler>()
            .HandleAsync(w.MemberId, default);
        Assert.Contains(list, s => s.ExerciseId == w.LoadExerciseId);

        var detail = await scope.ServiceProvider.GetRequiredService<GetMemberExerciseProgressHandler>()
            .HandleAsync(w.MemberId, w.LoadExerciseId, default);
        Assert.Single(detail.Points);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            scope.ServiceProvider.GetRequiredService<GetMemberProgressHandler>().HandleAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task A_member_only_sees_their_own_progress()
    {
        var w = await SeedAsync();
        await LogAsync(w, new DateOnly(2026, 6, 1), w.LoadLine, 80m, 5);

        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(w.OtherMemberUserId, w.GymId, UserRole.Member);

        var list = await scope.ServiceProvider.GetRequiredService<GetMyProgressHandler>().HandleAsync(default);
        Assert.Empty(list);
    }

    // ---- Apoyo ---------------------------------------------------------------------------------------------

    private static RoutineSetEntry Set(SetType type, int? reps, decimal? weight)
        => new(0, type, reps, weight, null, null, null);

    private IServiceScope AsMember(World w, DateOnly day)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(w.MemberUserId, w.GymId, UserRole.Member);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(day);
        return scope;
    }

    private async Task LogAsync(World w, DateOnly day, Guid line, decimal? weight, int? reps, int? durationSeconds = null)
    {
        using var scope = AsMember(w, day);
        await scope.ServiceProvider.GetRequiredService<LogRoutineExerciseHandler>().HandleAsync(
            line,
            new LogRoutineExerciseRequest(null, [new LogSetRequest("normal", reps, weight, durationSeconds, null, null)]),
            default);
    }

    private async Task<World> SeedAsync()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, members.members restart identity cascade;");
        await db.Database.ExecuteSqlRawAsync("delete from routines.routine_exercise_logs; delete from routines.routines;");

        var slug = $"prg-{Guid.NewGuid():N}"[..12];
        var gym = Gym.Create("Progreso Gym", slug);
        var staff = MakeUser($"{slug}-s");
        gym.AssignOwner(staff.Id);
        var memberUser = MakeUser($"{slug}-m");
        var otherUser = MakeUser($"{slug}-o");
        var member = Member.Create(gym.Id, "Miembro", "Progreso", $"doc-{slug}-1", new DateOnly(2026, 1, 1));
        var other = Member.Create(gym.Id, "Otro", "Progreso", $"doc-{slug}-2", new DateOnly(2026, 1, 1));
        member.LinkUser(memberUser.Id);
        other.LinkUser(otherUser.Id);

        db.AddRange(gym, GymFeatureFlags.Default(gym.Id), staff, memberUser, otherUser, member, other);
        await db.SaveChangesAsync();

        var loadExercise = await db.Set<Exercise>()
            .Where(e => e.GymId == null && e.TrackingType == TrackingType.RepsWeight)
            .OrderBy(e => e.Name).Select(e => e.Id).FirstAsync();
        var timeExercise = await db.Set<Exercise>()
            .Where(e => e.GymId == null && e.TrackingType == TrackingType.Time)
            .OrderBy(e => e.Name).Select(e => e.Id).FirstAsync();

        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(staff.Id, gym.Id, UserRole.Staff);
        var request = new SaveRoutineRequest("Rutina", null,
        [
            new RoutineDayInput("D1", false,
            [
                new RoutineExerciseInput(loadExercise, TestSets.Normal(3, 10), 60, null),
                new RoutineExerciseInput(timeExercise, TestSets.Normal(1, 1), 60, null),
            ]),
            new RoutineDayInput(null, true, []),
        ]);
        var routine = await scope.ServiceProvider.GetRequiredService<AssignRoutineHandler>()
            .HandleAsync(member.Id, request, default);
        var lines = routine.Days.Single(d => !d.IsRest).Exercises.OrderBy(e => e.Order).Select(e => e.Id).ToList();

        scope.Dispose();
        return new World(gym.Id, member.Id, memberUser.Id, otherUser.Id, staff.Id, lines[0], lines[1], loadExercise, timeExercise);
    }

    private static GSM.Infrastructure.Identity.AppUser MakeUser(string local)
        => new()
        {
            UserName = $"{local}@test.local",
            Email = $"{local}@test.local",
            NormalizedUserName = $"{local}@TEST.LOCAL",
            NormalizedEmail = $"{local}@TEST.LOCAL",
            EmailConfirmed = true,
            FullName = local,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        };

    private sealed record World(
        Guid GymId, Guid MemberId, Guid MemberUserId, Guid OtherMemberUserId, Guid StaffUserId,
        Guid LoadLine, Guid TimeLine, Guid LoadExerciseId, Guid TimeExerciseId);
}
