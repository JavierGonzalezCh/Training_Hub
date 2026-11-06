using GSM.Application.MemberStreak;
using GSM.Application.Routines;
using GSM.Application.Routines.Adherence;
using GSM.Application.Routines.Contracts;
using GSM.Application.Routines.MissedSession;
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

/// <summary>SP2–SP4: recuperar la sesión perdida de ayer, contra Postgres y con el reloj del gym (Colombia).</summary>
public sealed class MissedSessionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Monday = new(2026, 3, 2);

    private static DateOnly Day(int offset) => Monday.AddDays(offset);

    // L Pecho · M Pierna · X Espalda · V Hombro (jueves, sábado y domingo descanso).
    private async Task<(World W, RoutineResponse Routine)> SeedPlanAsync()
    {
        var w = await SeedAsync();
        var routine = await AssignAsync(w, WeeklyRequest(w, (1, "Pecho"), (2, "Pierna"), (3, "Espalda"), (5, "Hombro")));
        return (w, routine);
    }

    private static RoutineDayResponse DayNamed(RoutineResponse r, string name) => r.Days.Single(d => d.Name == name);

    private async Task<MyRoutineResponse> MineAsync(World w, DateOnly today)
    {
        using var scope = AsMember(w, today);
        return await scope.ServiceProvider.GetRequiredService<GetMyRoutineHandler>().HandleAsync(default);
    }

    private async Task<MyRoutineResponse> RecoverAsync(World w, DateOnly today, Guid dayId, bool reorganize)
    {
        using var scope = AsMember(w, today);
        return await scope.ServiceProvider.GetRequiredService<RecoverMissedSessionHandler>()
            .HandleAsync(new RecoverMissedSessionRequest(dayId, reorganize), default);
    }

    private async Task<MemberAdherence> AdherenceAsync(World w, DateOnly today)
    {
        using var scope = AsMember(w, today);
        return await scope.ServiceProvider.GetRequiredService<GetMyAdherenceHandler>().HandleAsync(default);
    }

    [Fact]
    public async Task Missing_monday_is_offered_on_tuesday_and_swapping_makes_tuesday_count_with_chest()
    {
        var (w, routine) = await SeedPlanAsync();
        var chest = DayNamed(routine, "Pecho");

        var tuesday = await MineAsync(w, Day(1));
        var missed = tuesday.MissedSession!;
        Assert.Equal(Day(0), missed.MissedOn);
        Assert.Equal(chest.Id, missed.MissedDayId);
        Assert.False(missed.TodayIsRest);
        Assert.Equal("Pierna", missed.Swap!.LostDayName);
        Assert.Equal("Hombro", missed.Reorganize!.LostDayName);
        Assert.Equal("Pierna", tuesday.Today!.DayName);

        var after = await RecoverAsync(w, Day(1), chest.Id, reorganize: false);

        Assert.Null(after.MissedSession);
        Assert.Equal("Pecho", after.Today!.DayName);
        Assert.Equal("Pierna", after.Today.PlannedDayName);
        Assert.Equal(chest.Id, after.Today.DayId);
        Assert.Equal(chest.Exercises[0].Id, Assert.Single(after.Today.Exercises).Id);

        await LogAsync(w, chest.Exercises[0].Id, Day(1));
        var days = (await AdherenceAsync(w, Day(1))).Weeks[^1].Days!;
        Assert.Equal("missed", days[0].Status);
        Assert.Equal("done", days[1].Status);
        Assert.True(days[1].IsChanged);
        Assert.Equal("Pierna", days[1].PlannedDayName);

        // El miércoles sigue tocando Espalda: solo cambiar no mueve el resto de la semana.
        Assert.Equal("Espalda", (await MineAsync(w, Day(2))).Today!.DayName);
    }

    [Fact]
    public async Task Reorganizing_shifts_the_week_keeping_rest_days_and_the_home_card_agrees()
    {
        var (w, routine) = await SeedPlanAsync();
        var chest = DayNamed(routine, "Pecho");

        await RecoverAsync(w, Day(1), chest.Id, reorganize: true);

        Assert.Equal("Pecho", (await MineAsync(w, Day(1))).Today!.DayName);
        Assert.Equal("Pierna", (await MineAsync(w, Day(2))).Today!.DayName);
        Assert.True((await MineAsync(w, Day(3))).Today!.IsRest);
        Assert.Equal("Espalda", (await MineAsync(w, Day(4))).Today!.DayName);
        Assert.Equal("Pecho", (await MineAsync(w, Day(7))).Today!.DayName); // la semana siguiente vuelve al plan

        using var scope = AsMember(w, Day(2));
        var home = await scope.ServiceProvider.GetRequiredService<GSM.Application.Routines.Public.IRoutineTodayReader>()
            .GetAsync(w.MemberId, default);
        Assert.Equal("Pierna", home!.DayName);
    }

    [Fact]
    public async Task Only_yesterdays_day_can_be_recovered_and_only_once()
    {
        var (w, routine) = await SeedPlanAsync();

        await Assert.ThrowsAsync<ValidationDomainException>(() =>
            RecoverAsync(w, Day(1), DayNamed(routine, "Espalda").Id, reorganize: false));

        await RecoverAsync(w, Day(1), DayNamed(routine, "Pecho").Id, reorganize: false);
        await Assert.ThrowsAsync<ConflictException>(() =>
            RecoverAsync(w, Day(1), DayNamed(routine, "Pecho").Id, reorganize: true));
    }

    [Fact]
    public async Task On_a_rest_day_it_is_only_suggested_and_starting_today_dismisses_it()
    {
        var (w, routine) = await SeedPlanAsync();

        // Faltó el miércoles; el jueves descansa: se sugiere, no hay nada que cambiar.
        var thursday = (await MineAsync(w, Day(3))).MissedSession!;
        Assert.True(thursday.TodayIsRest);
        Assert.Null(thursday.Swap);
        await Assert.ThrowsAsync<ValidationDomainException>(() =>
            RecoverAsync(w, Day(3), DayNamed(routine, "Espalda").Id, reorganize: false));

        // El martes empieza con la de hoy (4a): ya no se ofrece.
        await LogAsync(w, DayNamed(routine, "Pierna").Exercises[0].Id, Day(1));
        Assert.Null((await MineAsync(w, Day(1))).MissedSession);
    }

    [Fact]
    public async Task Recovering_does_not_forgive_the_missed_day_in_the_streak()
    {
        var (w, routine) = await SeedPlanAsync();
        await RecoverAsync(w, Day(1), DayNamed(routine, "Pecho").Id, reorganize: false);
        await AddAttendanceAsync(w, 1);

        // El lunes sigue como fallo; el martes cuenta. La racha es la misma que sin el cambio.
        Assert.Equal(1, await StreakAsync(w, Day(1)));
    }

    private static RoutineExerciseInput Line(World w, int catalogIndex)
        => new(w.CatalogIds[catalogIndex], TestSets.Normal(3, 10), 60, null);

    private static List<RoutineExerciseInput> KeepLines(RoutineDayResponse day)
        => day.Exercises.Select(e => new RoutineExerciseInput(e.ExerciseId, TestSets.Normal(3, 10), 60, null, Id: e.Id)).ToList();

    private static SaveRoutineRequest WeeklyRequest(World w, params (int Weekday, string Name)[] days)
        => new("Semanal", null,
            days.Select((d, i) => new RoutineDayInput(d.Name, false, [Line(w, i % 4)], null, d.Weekday)).ToList(),
            "weekly");

    private async Task<RoutineResponse> AssignAsync(World w, SaveRoutineRequest request)
    {
        using var scope = AsStaff(w, Monday);
        return await scope.ServiceProvider.GetRequiredService<AssignRoutineHandler>()
            .HandleAsync(w.MemberId, request, default);
    }

    private async Task<RoutineResponse> UpdateAsync(World w, Guid routineId, SaveRoutineRequest request)
    {
        using var scope = AsStaff(w, Monday);
        return await scope.ServiceProvider.GetRequiredService<UpdateRoutineHandler>()
            .HandleAsync(routineId, request, default);
    }

    private async Task<TodayView> TodayAsync(World w, DateOnly today)
    {
        using var scope = AsMember(w, today);
        var mine = await scope.ServiceProvider.GetRequiredService<GetMyRoutineHandler>().HandleAsync(default);
        return mine.Today!;
    }

    private async Task LogAsync(World w, Guid lineId, DateOnly on)
    {
        using var scope = AsMember(w, on);
        await scope.ServiceProvider.GetRequiredService<LogRoutineExerciseHandler>()
            .HandleAsync(lineId, new LogRoutineExerciseRequest(null, [new LogSetRequest("normal", 10, 20m, null, null, null)]), default);
    }

    private async Task<int> StreakAsync(World w, DateOnly today)
    {
        using var scope = AsStaff(w, today);
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

    private IServiceScope AsMember(World w, DateOnly today)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(w.MemberUserId, w.GymId, UserRole.Member);
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

        var slug = $"sp-{Guid.NewGuid():N}"[..12];
        var gym = Gym.Create("SP Gym", slug);
        var staff = MakeUser($"{slug}-s", UserRole.Staff, gym.Id);
        gym.AssignOwner(staff.Id);
        var memberUser = MakeUser($"{slug}-m", UserRole.Member, gym.Id);
        var member = Member.Create(gym.Id, "Miembro", "SP", $"doc-{slug}", new DateOnly(2026, 1, 1));
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
