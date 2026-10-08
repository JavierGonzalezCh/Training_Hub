using GSM.Application.MemberStreak;
using GSM.Application.Routines;
using GSM.Application.Routines.Contracts;
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

/// <summary>Racha contra el calendario de la rutina (etapa 9.4).</summary>
public sealed class MemberStreakTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Monday = new(2026, 3, 2); // ancla del ciclo [D1·D2·D3·descanso]

    [Fact]
    public async Task Two_full_cycles_training_and_resting_gives_streak_8()
    {
        var w = await SeedWithRoutineAsync();

        // Entrena D1,D2,D3 de cada vuelta (offsets 0,1,2 y 4,5,6); no va los descansos (3 y 7).
        await AddAttendanceAsync(w, [0, 1, 2, 4, 5, 6]);

        var streak = await StreakAsync(w, Monday.AddDays(7)); // hoy = descanso de la 2a vuelta
        Assert.Equal(8, streak);
    }

    [Fact]
    public async Task Missing_two_training_days_cuts_the_streak_once_the_single_freeze_runs_out()
    {
        var w = await SeedWithRoutineAsync();
        await AddAttendanceAsync(w, [0, 1, 2, 4]); // faltan offsets 5 y 6

        // El congelador (concesión perezosa) absorbe el fallo más reciente; el segundo corta.
        var streak = await StreakAsync(w, Monday.AddDays(7));
        Assert.Equal(1, streak); // solo el descanso de hoy
    }

    [Fact]
    public async Task Today_being_an_unvisited_training_day_does_not_penalize()
    {
        var w = await SeedWithRoutineAsync();
        await AddAttendanceAsync(w, [0, 1, 2]); // primera vuelta completa, descanso el 3

        // Hoy = offset 4 (D1 de la 2a vuelta), aún sin ir: racha = la de ayer.
        var today = await StreakAsync(w, Monday.AddDays(4));
        var yesterday = await StreakAsync(w, Monday.AddDays(3));

        Assert.Equal(4, yesterday); // D1,D2,D3,descanso
        Assert.Equal(yesterday, today);
    }

    [Fact]
    public async Task A_member_without_a_routine_keeps_the_classic_streak()
    {
        var w = await SeedWithRoutineAsync(assignRoutine: false);
        // Regla vieja: tolerancia de 1 día de descanso.
        await AddAttendanceAsync(w, [0, 1, 3, 4]);

        var streak = await StreakAsync(w, Monday.AddDays(5));
        Assert.Equal(4, streak);
    }

    private async Task<int> StreakAsync(World w, DateOnly today)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(w.StaffId, w.GymId, UserRole.Staff);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(today);
        var result = await scope.ServiceProvider.GetRequiredService<GetMemberStreakHandler>()
            .HandleAsync(w.MemberId, default);
        return result.CurrentStreak;
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

    private async Task<World> SeedWithRoutineAsync(bool assignRoutine = true)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, members.members, attendance.attendances restart identity cascade;");
        await db.Database.ExecuteSqlRawAsync("delete from routines.routine_exercise_logs; delete from routines.routines;");

        var slug = $"strk-{Guid.NewGuid():N}"[..12];
        var gym = Gym.Create("Streak Gym", slug);
        var staff = MakeUser($"{slug}-s", UserRole.Staff, gym.Id);
        gym.AssignOwner(staff.Id);
        var member = Member.Create(gym.Id, "Miembro", "Racha", $"doc-{slug}", new DateOnly(2026, 1, 1));

        db.AddRange(gym, GymFeatureFlags.Default(gym.Id), staff, member);
        await db.SaveChangesAsync();

        if (assignRoutine)
        {
            var exerciseId = await db.Set<Exercise>().Where(e => e.GymId == null).Select(e => e.Id).FirstAsync();
            scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(staff.Id, gym.Id, UserRole.Staff);
            scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Monday);

            var request = new SaveRoutineRequest("PPL", null,
            [
                new RoutineDayInput("D1", false, [new RoutineExerciseInput(exerciseId, 3, "10", null, 60, null)]),
                new RoutineDayInput("D2", false, [new RoutineExerciseInput(exerciseId, 3, "10", null, 60, null)]),
                new RoutineDayInput("D3", false, [new RoutineExerciseInput(exerciseId, 3, "10", null, 60, null)]),
                new RoutineDayInput(null, true, []),
            ]);
            await scope.ServiceProvider.GetRequiredService<AssignRoutineHandler>()
                .HandleAsync(member.Id, request, default);
        }

        scope.Dispose();
        return new World(gym.Id, staff.Id, member.Id);
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

    private sealed record World(Guid GymId, Guid StaffId, Guid MemberId);
}
