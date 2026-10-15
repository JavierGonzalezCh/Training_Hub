using GSM.Application.MemberStreak;
using GSM.Application.Routines;
using GSM.Application.Routines.Contracts;
using GSM.Domain.Achievements;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Routines;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using AttendanceEntity = GSM.Domain.Attendance.Attendance;
using AttendanceMethodEnum = GSM.Domain.Attendance.AttendanceMethod;

namespace GSM.IntegrationTests;

/// <summary>Congelador de racha (etapa 9.5).</summary>
public sealed class StreakFreezeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Monday = new(2026, 3, 2);

    [Fact]
    public async Task A_freeze_covers_a_missed_training_day_and_is_marked_with_that_date()
    {
        var w = await SeedAsync(withRoutine: true);
        await AddAttendanceAsync(w, [0, 1, 2, 4, 6]); // falta offset 5 (D2 de la 2a vuelta)

        var result = await StreakAsync(w, Monday.AddDays(7));

        Assert.Equal(7, result.CurrentStreak); // la racha no se corta
        Assert.Equal(0, result.FreezesAvailable);

        var freezes = await FreezesAsync(w);
        Assert.Single(freezes);
        Assert.Equal(Monday.AddDays(5), freezes[0].UsedOn);
    }

    [Fact]
    public async Task A_second_miss_without_balance_cuts_the_streak()
    {
        var w = await SeedAsync(withRoutine: true);
        await AddAttendanceAsync(w, [0, 1, 2, 4]); // faltan offsets 5 y 6

        var result = await StreakAsync(w, Monday.AddDays(7));

        Assert.Equal(1, result.CurrentStreak); // solo el descanso de hoy; el 2o fallo corta
        var freezes = await FreezesAsync(w);
        Assert.Single(freezes);
        Assert.Equal(Monday.AddDays(6), freezes[0].UsedOn);
    }

    [Fact]
    public async Task Querying_twice_does_not_spend_two_freezes_for_the_same_failed_day()
    {
        var w = await SeedAsync(withRoutine: true);
        await AddAttendanceAsync(w, [0, 1, 2, 4, 6]);

        var first = await StreakAsync(w, Monday.AddDays(7));
        var second = await StreakAsync(w, Monday.AddDays(7));

        Assert.Equal(first.CurrentStreak, second.CurrentStreak);
        Assert.Equal(7, second.CurrentStreak);

        var freezes = await FreezesAsync(w);
        Assert.Single(freezes); // sigue habiendo uno solo, gastado en la misma fecha
        Assert.Equal(Monday.AddDays(5), freezes[0].UsedOn);
    }

    [Fact]
    public async Task Balance_never_exceeds_two_however_many_days_pass()
    {
        var w = await SeedAsync(withRoutine: false);

        await StreakAsync(w, Monday);            // otorga el 1o
        await StreakAsync(w, Monday.AddDays(20)); // otorga el 2o
        var third = await StreakAsync(w, Monday.AddDays(60)); // ya no otorga

        Assert.Equal(2, third.FreezesAvailable);
        var freezes = await FreezesAsync(w);
        Assert.Equal(2, freezes.Count);
    }

    private async Task<MemberStreakResponse> StreakAsync(World w, DateOnly today)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(w.StaffId, w.GymId, UserRole.Staff);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(today);
        return await scope.ServiceProvider.GetRequiredService<GetMemberStreakHandler>()
            .HandleAsync(w.MemberId, default);
    }

    private async Task<List<StreakFreeze>> FreezesAsync(World w)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        return await scope.ServiceProvider.GetRequiredService<GsmDbContext>()
            .Set<StreakFreeze>().Where(f => f.MemberId == w.MemberId).OrderBy(f => f.GrantedOn).ToListAsync();
    }

    private async Task AddAttendanceAsync(World w, int[] offsets)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        foreach (var offset in offsets)
        {
            var date = Monday.AddDays(offset);
            db.Add(AttendanceEntity.Create(
                w.GymId, w.MemberId, AttendanceMethodEnum.Manual,
                new DateTimeOffset(date.ToDateTime(new TimeOnly(23, 0)), TimeSpan.Zero), date));
        }

        await db.SaveChangesAsync();
    }

    private async Task<World> SeedAsync(bool withRoutine)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, members.members, attendance.attendances, achievements.streak_freezes restart identity cascade;");
        await db.Database.ExecuteSqlRawAsync("delete from routines.routine_exercise_logs; delete from routines.routines;");

        var slug = $"frz-{Guid.NewGuid():N}"[..12];
        var gym = Gym.Create("Freeze Gym", slug);
        var staff = new GSM.Infrastructure.Identity.AppUser
        {
            UserName = $"{slug}@test.local",
            Email = $"{slug}@test.local",
            NormalizedUserName = $"{slug}@TEST.LOCAL",
            NormalizedEmail = $"{slug}@TEST.LOCAL",
            EmailConfirmed = true,
            FullName = "Freeze Staff",
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        };
        gym.AssignOwner(staff.Id);
        var member = Member.Create(gym.Id, "Miembro", "Freeze", $"doc-{slug}", new DateOnly(2026, 1, 1));

        db.AddRange(gym, GymFeatureFlags.Default(gym.Id), staff, member);
        await db.SaveChangesAsync();

        if (withRoutine)
        {
            var exerciseId = await db.Set<Exercise>().Where(e => e.GymId == null).Select(e => e.Id).FirstAsync();
            scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(staff.Id, gym.Id, UserRole.Staff);
            scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Monday);
            var request = new SaveRoutineRequest("PPL", null,
            [
                new RoutineDayInput("D1", false, [new RoutineExerciseInput(exerciseId, TestSets.Normal(3, 10), 60, null)]),
                new RoutineDayInput("D2", false, [new RoutineExerciseInput(exerciseId, TestSets.Normal(3, 10), 60, null)]),
                new RoutineDayInput("D3", false, [new RoutineExerciseInput(exerciseId, TestSets.Normal(3, 10), 60, null)]),
                new RoutineDayInput(null, true, []),
            ]);
            await scope.ServiceProvider.GetRequiredService<AssignRoutineHandler>()
                .HandleAsync(member.Id, request, default);
        }

        scope.Dispose();
        return new World(gym.Id, staff.Id, member.Id);
    }

    private sealed record World(Guid GymId, Guid StaffId, Guid MemberId);
}
