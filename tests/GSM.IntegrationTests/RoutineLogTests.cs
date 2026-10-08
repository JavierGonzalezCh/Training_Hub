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

public sealed class RoutineLogTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Logging_twice_the_same_day_updates_and_publishes_the_session_once()
    {
        var w = await SeedAsync(exercisesPerDay: 3);
        factory.Services.GetRequiredService<RoutineEventRecorder>().Clear();
        var day = new DateOnly(2026, 6, 10);

        using (var scope = AsMember(w, day))
        {
            var log = scope.ServiceProvider.GetRequiredService<LogRoutineExerciseHandler>();

            // Cinco registros el mismo día (repitiendo el primero dos veces).
            await log.HandleAsync(w.ExerciseLineIds[0], new LogRoutineExerciseRequest(3, "10", 20m, null), default);
            await log.HandleAsync(w.ExerciseLineIds[1], new LogRoutineExerciseRequest(null, null, null, null), default);
            await log.HandleAsync(w.ExerciseLineIds[2], new LogRoutineExerciseRequest(4, "8", 40m, null), default);
            var updated = await log.HandleAsync(
                w.ExerciseLineIds[0], new LogRoutineExerciseRequest(5, "12", 25m, "más peso"), default);

            Assert.Equal(5, updated.SetsDone);
            Assert.Equal("más peso", updated.Notes);
        }

        using (var scope = AsMember(w, day))
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            var count = await db.Set<RoutineExerciseLog>()
                .CountAsync(l => l.MemberId == w.MemberId && l.PerformedOn == day);
            Assert.Equal(3, count); // 3 líneas distintas, la repetición actualizó
        }

        var sessions = factory.Services.GetRequiredService<RoutineEventRecorder>().Sessions;
        Assert.Single(sessions, s => s.MemberId == w.MemberId && s.PerformedOn == day);
    }

    [Fact]
    public async Task A_log_at_22h_Bogota_counts_on_the_local_day_not_the_utc_one()
    {
        var w = await SeedAsync(exercisesPerDay: 1);
        var businessDay = new DateOnly(2026, 6, 10);
        // 22:00 en Bogotá (UTC-5) ya es el 11 a las 03:00 UTC.
        var instant = new DateTimeOffset(2026, 6, 11, 3, 0, 0, TimeSpan.Zero);

        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(w.MemberUserId, w.GymId, UserRole.Member);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinTo(instant, businessDay);

        var log = await scope.ServiceProvider.GetRequiredService<LogRoutineExerciseHandler>()
            .HandleAsync(w.ExerciseLineIds[0], new LogRoutineExerciseRequest(null, null, null, null), default);

        Assert.Equal(businessDay, log.PerformedOn);
        Assert.Equal(instant, log.CompletedAt);
    }

    [Fact]
    public async Task A_member_cannot_log_on_another_members_routine()
    {
        var w = await SeedAsync(exercisesPerDay: 1);

        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>()
            .ActAs(w.OtherMemberUserId, w.GymId, UserRole.Member);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(new DateOnly(2026, 6, 10));

        await Assert.ThrowsAsync<ForbiddenDomainException>(() =>
            scope.ServiceProvider.GetRequiredService<LogRoutineExerciseHandler>()
                .HandleAsync(w.ExerciseLineIds[0], new LogRoutineExerciseRequest(null, null, null, null), default));
    }

    [Fact]
    public async Task Per_set_weights_are_stored_derive_the_totals_and_come_back_in_the_history()
    {
        var w = await SeedAsync(exercisesPerDay: 1);
        var day = new DateOnly(2026, 6, 12);

        using (var scope = AsMember(w, day))
        {
            var log = await scope.ServiceProvider.GetRequiredService<LogRoutineExerciseHandler>().HandleAsync(
                w.ExerciseLineIds[0],
                new LogRoutineExerciseRequest(null, null, null, null,
                [
                    new LogSetRequest(10, 40m),
                    new LogSetRequest(8, 45m),
                    new LogSetRequest(6, 50m),
                ]),
                default);

            Assert.Equal(3, log.SetsDone);
            Assert.Equal(50m, log.WeightKg);          // el mayor peso, para el progreso
            Assert.Equal("10/8/6", log.RepsDone);
            Assert.Equal([1, 2, 3], log.Sets!.Select(s => s.Number));

            // Repetir el mismo día reemplaza el detalle.
            var again = await scope.ServiceProvider.GetRequiredService<LogRoutineExerciseHandler>().HandleAsync(
                w.ExerciseLineIds[0],
                new LogRoutineExerciseRequest(null, null, null, null, [new LogSetRequest(12, 30m)]),
                default);
            Assert.Single(again.Sets!);
            Assert.Equal(30m, again.WeightKg);
        }

        using (var scope = AsMember(w, day))
        {
            var history = await scope.ServiceProvider.GetRequiredService<GetMyRoutineHistoryHandler>()
                .HandleAsync(day, day, default);
            var item = Assert.Single(history);
            var set = Assert.Single(item.Sets!);
            Assert.Equal((12, 30m), (set.Reps, set.WeightKg));
        }

        using (var scope = AsMember(w, day))
        {
            await Assert.ThrowsAsync<ValidationDomainException>(() =>
                scope.ServiceProvider.GetRequiredService<LogRoutineExerciseHandler>().HandleAsync(
                    w.ExerciseLineIds[0],
                    new LogRoutineExerciseRequest(null, null, null, null, [new LogSetRequest(10, -5m)]),
                    default));
        }
    }

    private IServiceScope AsMember(World w, DateOnly day)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(w.MemberUserId, w.GymId, UserRole.Member);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(day);
        return scope;
    }

    private async Task<World> SeedAsync(int exercisesPerDay)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, members.members restart identity cascade;");
        await db.Database.ExecuteSqlRawAsync("delete from routines.routine_exercise_logs; delete from routines.routines;");

        var slug = $"log-{Guid.NewGuid():N}"[..12];
        var gym = Gym.Create("Log Gym", slug);
        var staff = MakeUser($"{slug}-s", UserRole.Staff, gym.Id);
        gym.AssignOwner(staff.Id);
        var memberUser = MakeUser($"{slug}-m", UserRole.Member, gym.Id);
        var otherUser = MakeUser($"{slug}-o", UserRole.Member, gym.Id);
        var member = Member.Create(gym.Id, "Miembro", "Log", $"doc-{slug}-1", new DateOnly(2026, 1, 1));
        var other = Member.Create(gym.Id, "Otro", "Log", $"doc-{slug}-2", new DateOnly(2026, 1, 1));
        member.LinkUser(memberUser.Id);
        other.LinkUser(otherUser.Id);

        db.AddRange(gym, GymFeatureFlags.Default(gym.Id), staff, memberUser, otherUser, member, other);
        await db.SaveChangesAsync();

        var exerciseIds = await db.Set<Exercise>().Where(e => e.GymId == null)
            .OrderBy(e => e.Name).Select(e => e.Id).Take(exercisesPerDay).ToListAsync();

        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(staff.Id, gym.Id, UserRole.Staff);
        var request = new SaveRoutineRequest("Rutina", null,
        [
            new RoutineDayInput("D1", false,
                exerciseIds.Select(id => new RoutineExerciseInput(id, 3, "10", null, 60, null)).ToList()),
            new RoutineDayInput(null, true, []),
        ]);
        var routine = await scope.ServiceProvider.GetRequiredService<AssignRoutineHandler>()
            .HandleAsync(member.Id, request, default);

        var lineIds = routine.Days.Single(d => !d.IsRest).Exercises.OrderBy(e => e.Order).Select(e => e.Id).ToList();

        scope.Dispose();
        return new World(gym.Id, member.Id, memberUser.Id, otherUser.Id, lineIds);
    }

    private static GSM.Infrastructure.Identity.AppUser MakeUser(string local, UserRole role, Guid gymId)
        => new()
        {
            UserName = $"{local}@test.local",
            Email = $"{local}@test.local",
            NormalizedUserName = $"{local}@TEST.LOCAL",
            NormalizedEmail = $"{local}@TEST.LOCAL",
            EmailConfirmed = true,
            FullName = local,
            Role = role,
            GymId = gymId,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        };

    private sealed record World(
        Guid GymId, Guid MemberId, Guid MemberUserId, Guid OtherMemberUserId, IReadOnlyList<Guid> ExerciseLineIds);
}
