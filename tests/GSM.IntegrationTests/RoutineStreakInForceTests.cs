using GSM.Application.Attendance;
using GSM.Application.MemberStreak;
using GSM.Application.Routines;
using GSM.Application.Routines.Contracts;
using GSM.Application.Routines.Public;
using GSM.Domain.Common.Exceptions;
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

/// <summary>R2b: la racha juzga cada fecha con los días y la rutina vigentes ese día.</summary>
public sealed class RoutineStreakInForceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Monday = new(2026, 3, 2);

    private static DateOnly Day(int offset) => Monday.AddDays(offset);

    [Fact]
    public async Task Retiring_a_training_day_today_does_not_change_the_streak_over_past_dates()
    {
        var w = await SeedAsync();
        var weekly = await AssignAsync(w, WeeklyRequest(w, (1, "A"), (3, "B"), (5, "C")));
        await AddAttendanceAsync(w, 0, 2); // lunes y miércoles; falta el viernes

        // Sábado: el viernes de entrenamiento sin asistencia (lo puede cubrir el congelador).
        var before = await StreakAsync(w, 5);

        // El sábado se retira el viernes: las fechas pasadas no cambian.
        var request = new SaveRoutineRequest("Semanal", null,
        [
            new RoutineDayInput("A", false, KeepLines(weekly.Days[0]), weekly.Days[0].Id, 1),
            new RoutineDayInput("B", false, KeepLines(weekly.Days[1]), weekly.Days[1].Id, 3),
        ]);
        await UpdateAsync(w, weekly.Id, request, onDay: 5);

        Assert.Equal(before, await StreakAsync(w, 5));
    }

    [Fact]
    public async Task Changing_routine_today_keeps_the_past_streak_and_uses_the_new_one_from_the_change()
    {
        var w = await SeedAsync();
        await AssignAsync(w, WeeklyRequest(w, (1, "A"), (3, "B"), (5, "C")));
        await AddAttendanceAsync(w, 0, 2, 4, 7); // la semana completa y el lunes siguiente

        // Lunes siguiente: rutina nueva solo de lunes. La racha ganada (lunes a lunes = 8 días) se conserva.
        await AssignAsync(w, WeeklyRequest(w, (1, "Solo lunes")), onDay: 7);

        Assert.Equal(8, await StreakAsync(w, 7));
    }

    [Fact]
    public async Task Without_a_routine_the_streak_is_the_classic_one()
    {
        var w = await SeedAsync();
        await AddAttendanceAsync(w, 0, 1, 3);

        Assert.Equal(3, await StreakAsync(w, 3));
    }

    [Fact]
    public async Task A_planned_rest_day_in_force_does_not_break_the_streak()
    {
        var w = await SeedAsync();
        await AssignAsync(w, WeeklyRequest(w, (1, "A"), (4, "B"))); // martes, miércoles, viernes y fin de semana: descanso
        await AddAttendanceAsync(w, 0, 3);

        // Domingo (Day 6): descanso, descanso, jueves entrenado, descanso, descanso, lunes entrenado.
        Assert.Equal(7, await StreakAsync(w, 6));

        // Retirar el jueves el domingo no vuelve el pasado un hueco ni lo cambia.
        var current = await AssignAsync(w, WeeklyRequest(w, (1, "A")), onDay: 7);
        // Hoy (lunes, sin asistencia aún) queda pendiente y no suma.
        Assert.Equal(7, await StreakAsync(w, 7));
        Assert.NotEqual(Guid.Empty, current.Id);
    }

    [Fact]
    public async Task Rotating_cycle_is_rebuilt_with_the_days_in_force_on_each_date()
    {
        var w = await SeedAsync();
        // Rotativa desde el lunes: A, descanso, A, descanso…
        var rotating = await AssignAsync(w, new SaveRoutineRequest("Rotativa", null,
        [
            new RoutineDayInput("A", false, [Line(w, 0)], null, null),
            new RoutineDayInput("Descanso", true, [], null, null),
        ]));
        await AddAttendanceAsync(w, 0, 2, 4, 5);

        // El sábado se retira el descanso: desde ese día todos los días son de A.
        await UpdateAsync(w, rotating.Id, new SaveRoutineRequest("Rotativa", null,
        [
            new RoutineDayInput("A", false, KeepLines(rotating.Days[0]), rotating.Days[0].Id, null),
        ]), onDay: 5);

        // Los martes y jueves pasados siguen siendo descanso: lunes a sábado = 6.
        Assert.Equal(6, await StreakAsync(w, 5));
    }

    private static RoutineExerciseInput Line(World w, int catalogIndex)
        => new(w.CatalogIds[catalogIndex], TestSets.Normal(3, 10), 60, null);

    private static List<RoutineExerciseInput> KeepLines(RoutineDayResponse day)
        => day.Exercises.Select(e => new RoutineExerciseInput(e.ExerciseId, TestSets.Normal(3, 10), 60, null, Id: e.Id)).ToList();

    private static SaveRoutineRequest WeeklyRequest(World w, params (int Weekday, string Name)[] days)
        => new("Semanal", null,
            days.Select((d, i) => new RoutineDayInput(d.Name, false, [Line(w, i % 4)], null, d.Weekday)).ToList(),
            "weekly");

    private async Task<RoutineResponse> AssignAsync(World w, SaveRoutineRequest request, int onDay = 0)
    {
        using var scope = AsStaff(w, Day(onDay));
        return await scope.ServiceProvider.GetRequiredService<AssignRoutineHandler>()
            .HandleAsync(w.MemberId, request, default);
    }

    private async Task<RoutineResponse> UpdateAsync(World w, Guid routineId, SaveRoutineRequest request, int onDay = 0)
    {
        using var scope = AsStaff(w, Day(onDay));
        return await scope.ServiceProvider.GetRequiredService<UpdateRoutineHandler>()
            .HandleAsync(routineId, request, default);
    }

    private async Task<int> StreakAsync(World w, int onDay)
    {
        using var scope = AsStaff(w, Day(onDay));
        return (await scope.ServiceProvider.GetRequiredService<GetMemberStreakHandler>()
            .HandleAsync(w.MemberId, default)).CurrentStreak;
    }

    private async Task AddAttendanceAsync(World w, params int[] offsets)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        foreach (var offset in offsets)
        {
            var date = Day(offset);
            db.Add(AttendanceEntity.Create(
                w.GymId, w.MemberId, AttendanceMethodEnum.Manual,
                new DateTimeOffset(date.ToDateTime(new TimeOnly(23, 0)), TimeSpan.Zero), date));
        }

        await db.SaveChangesAsync();
    }

    private IServiceScope AsStaff(World w, DateOnly today)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(w.StaffId, w.GymId, UserRole.Staff);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(today);
        return scope;
    }

    private async Task<World> SeedAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, members.members, attendance.attendances restart identity cascade;");
        await db.Database.ExecuteSqlRawAsync("delete from routines.routine_exercise_logs; delete from routines.routines;");

        var slug = $"r2-{Guid.NewGuid():N}"[..12];
        var gym = Gym.Create("R2 Gym", slug);
        var staff = MakeUser($"{slug}-s", UserRole.Staff, gym.Id);
        gym.AssignOwner(staff.Id);
        var memberUser = MakeUser($"{slug}-m", UserRole.Member, gym.Id);
        var member = Member.Create(gym.Id, "Miembro", "R2", $"doc-{slug}", new DateOnly(2026, 1, 1));
        member.LinkUser(memberUser.Id);

        db.AddRange(gym, GymFeatureFlags.Default(gym.Id), staff, memberUser, member);
        await db.SaveChangesAsync();

        var catalog = await db.Set<Exercise>().Where(e => e.GymId == null)
            .OrderBy(e => e.Name).Select(e => e.Id).Take(4).ToListAsync();

        return new World(gym.Id, staff.Id, member.Id, memberUser.Id, catalog);
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
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        };

    private sealed record World(
        Guid GymId, Guid StaffId, Guid MemberId, Guid MemberUserId, IReadOnlyList<Guid> CatalogIds);
}
