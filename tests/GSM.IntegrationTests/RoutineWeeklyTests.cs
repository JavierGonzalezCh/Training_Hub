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

/// <summary>R2: modo de calendario semanal (lunes a domingo) además del ciclo rotativo, y la racha contra él.</summary>
public sealed class RoutineWeeklyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Monday = new(2026, 3, 2);

    private static DateOnly Day(int offset) => Monday.AddDays(offset);

    // --- migración y compatibilidad -----------------------------------------------------------

    [Fact]
    public async Task Existing_routines_are_rotating_and_the_column_defaults_to_rotating()
    {
        var w = await SeedAsync();

        // Sin modo en el body (cliente anterior a R2) la rutina es rotativa.
        var routine = await AssignAsync(w, new SaveRoutineRequest("Vieja", null,
        [
            new RoutineDayInput("D1", false, [Line(w, 0)]),
            new RoutineDayInput(null, true, []),
        ]));
        Assert.Equal("rotating", routine.ScheduleMode);
        Assert.All(routine.Days, d => Assert.Null(d.DayOfWeek));

        // Las filas que ya existían al migrar toman el valor por defecto de la columna.
        using var scope = AsStaff(w, Monday);
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var columnDefault = await db.Database
            .SqlQueryRaw<string>(
                "select column_default as \"Value\" from information_schema.columns " +
                "where table_schema = 'routines' and table_name = 'routines' and column_name = 'schedule_mode'")
            .SingleAsync();
        Assert.Contains("rotating", columnDefault);
    }

    [Fact]
    public async Task A_rotating_routine_still_resolves_today_by_cycle_position()
    {
        var w = await SeedAsync();
        await AssignAsync(w, new SaveRoutineRequest("Ciclo", null,
        [
            new RoutineDayInput("D1", false, [Line(w, 0)]),
            new RoutineDayInput(null, true, []),
        ], ScheduleMode: "rotating"));

        Assert.False((await TodayAsync(w, Day(0))).IsRest);
        Assert.True((await TodayAsync(w, Day(1))).IsRest);
        Assert.False((await TodayAsync(w, Day(2))).IsRest);
    }

    // --- creación y validación ----------------------------------------------------------------

    [Fact]
    public async Task A_weekly_routine_binds_each_day_to_a_weekday_and_orders_them_monday_to_sunday()
    {
        var w = await SeedAsync();

        // Se envían desordenados a propósito: el viernes antes que el lunes.
        var routine = await AssignAsync(w, WeeklyRequest(w, (5, "Pierna"), (1, "Empuje"), (3, "Tirón")));

        Assert.Equal("weekly", routine.ScheduleMode);
        Assert.Equal(3, routine.CycleLength);
        Assert.Equal([1, 3, 5], routine.Days.Select(d => d.DayOfWeek!.Value));
        Assert.Equal(["Empuje", "Tirón", "Pierna"], routine.Days.Select(d => d.Name));
        Assert.Equal([1, 2, 3], routine.Days.Select(d => d.Position));
        Assert.All(routine.Days, d => Assert.False(d.IsRest));
    }

    [Fact]
    public async Task Repeating_a_weekday_is_a_422()
    {
        var w = await SeedAsync();

        await Assert.ThrowsAsync<ValidationDomainException>(() =>
            AssignAsync(w, WeeklyRequest(w, (1, "A"), (3, "B"), (1, "C"))));
    }

    [Fact]
    public async Task A_weekly_day_without_weekday_or_with_a_rest_row_is_a_422_and_a_rotating_one_cannot_carry_a_weekday()
    {
        var w = await SeedAsync();

        await Assert.ThrowsAsync<ValidationDomainException>(() => AssignAsync(w, new SaveRoutineRequest("S", null,
            [new RoutineDayInput("A", false, [Line(w, 0)])], "weekly")));

        await Assert.ThrowsAsync<ValidationDomainException>(() => AssignAsync(w, new SaveRoutineRequest("S", null,
            [new RoutineDayInput("A", false, [Line(w, 0)], null, 1), new RoutineDayInput(null, true, [], null, 2)],
            "weekly")));

        await Assert.ThrowsAsync<ValidationDomainException>(() => AssignAsync(w, new SaveRoutineRequest("R", null,
            [new RoutineDayInput("A", false, [Line(w, 0)], null, 1)], "rotating")));

        await Assert.ThrowsAsync<ValidationDomainException>(() => AssignAsync(w, new SaveRoutineRequest("S", null,
            [new RoutineDayInput("A", false, [Line(w, 0)], null, 9)], "weekly")));

        await Assert.ThrowsAsync<ValidationDomainException>(() => AssignAsync(w, new SaveRoutineRequest("S", null,
            [new RoutineDayInput("A", false, [Line(w, 0)], null, 1)], "mensual")));
    }

    // --- la rutina de hoy ---------------------------------------------------------------------

    [Fact]
    public async Task Today_in_weekly_mode_is_the_day_of_todays_weekday_or_rest_when_it_has_no_row()
    {
        var w = await SeedAsync();
        await AssignAsync(w, WeeklyRequest(w, (1, "Empuje"), (3, "Tirón"), (5, "Pierna")));

        var monday = await TodayAsync(w, Day(0));
        Assert.False(monday.IsRest);
        Assert.Equal("Empuje", monday.DayName);
        Assert.Equal(1, monday.DayOfWeek);
        Assert.Single(monday.Exercises);

        var tuesday = await TodayAsync(w, Day(1));
        Assert.True(tuesday.IsRest);
        Assert.Empty(tuesday.Exercises);
        Assert.Equal(2, tuesday.DayOfWeek);

        Assert.Equal("Pierna", (await TodayAsync(w, Day(4))).DayName);
        Assert.True((await TodayAsync(w, Day(6))).IsRest); // domingo

        // La semana siguiente se repite: no depende del ancla.
        Assert.Equal("Tirón", (await TodayAsync(w, Day(9))).DayName);
    }

    // --- racha --------------------------------------------------------------------------------

    [Fact]
    public async Task A_planned_rest_weekday_does_not_break_the_streak()
    {
        var w = await SeedAsync();
        await AssignAsync(w, WeeklyRequest(w, (1, "Empuje"), (3, "Tirón"), (5, "Pierna")));
        await AddAttendanceAsync(w, 0, 2); // lunes y miércoles; el martes es descanso

        // Hoy miércoles: miércoles + martes (descanso) + lunes; el domingo anterior ya no pertenece a la rutina.
        Assert.Equal(3, await StreakAsync(w, Day(2)));
    }

    [Fact]
    public async Task A_missed_training_weekday_breaks_the_calendar_streak_but_a_rest_weekday_never_does()
    {
        var w = await SeedAsync();
        await AssignAsync(w, WeeklyRequest(w, (1, "Empuje"), (3, "Tirón"), (5, "Pierna")));

        using var scope = AsStaff(w, Day(13));
        var calendar = await scope.ServiceProvider.GetRequiredService<IRoutineReader>()
            .GetTrainingCalendarAsync(w.MemberId, Day(7), Day(13));

        // Semana del 9 al 15 de marzo: lunes/miércoles/viernes se entrena; el resto es descanso.
        Assert.Equal([Day(7), Day(9), Day(11)], calendar.TrainingDays.OrderBy(d => d));
        Assert.Equal([Day(8), Day(10), Day(12), Day(13)], calendar.RestDays.OrderBy(d => d));

        // Sin congelador: faltar al lunes corta; el martes de descanso no.
        var attended = new HashSet<DateOnly> { Day(9), Day(11) }; // miércoles y viernes
        Assert.Equal(
            5, // hoy sábado (descanso), viernes, jueves (descanso), miércoles y martes (descanso); el lunes corta
            StreakCalculator.ComputeAgainstCalendar(attended, calendar.TrainingDays, calendar.RestDays, Day(12)));

        var rested = new HashSet<DateOnly> { Day(7), Day(9), Day(11) }; // lunes, miércoles y viernes
        Assert.Equal(
            6, // lunes a sábado; el domingo anterior queda fuera de la ventana consultada
            StreakCalculator.ComputeAgainstCalendar(rested, calendar.TrainingDays, calendar.RestDays, Day(12)));
    }

    // --- cambio de modo -----------------------------------------------------------------------

    [Fact]
    public async Task Changing_the_mode_creates_a_new_routine_and_deactivates_the_previous_one_keeping_its_logs()
    {
        var w = await SeedAsync();
        var rotating = await AssignAsync(w, new SaveRoutineRequest("Ciclo", null,
        [
            new RoutineDayInput("D1", false, [Line(w, 0), Line(w, 1)]),
            new RoutineDayInput(null, true, []),
        ]));
        var lineId = rotating.Days.First(d => !d.IsRest).Exercises[0].Id;
        await LogAsync(w, lineId);

        // El body trae ids de la rutina anterior: se ignoran.
        var asWeekly = WeeklyRequest(w, (1, "Empuje"), (4, "Pierna")) with { Name = "Semanal" };
        var replaced = await UpdateAsync(w, rotating.Id, asWeekly);

        Assert.NotEqual(rotating.Id, replaced.Id);
        Assert.Equal("weekly", replaced.ScheduleMode);
        Assert.True(replaced.IsActive);
        Assert.Equal(Monday, replaced.StartsOn);
        Assert.Equal([1, 4], replaced.Days.Select(d => d.DayOfWeek!.Value));

        using var scope = AsStaff(w, Monday);
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var old = await db.Set<Routine>().SingleAsync(r => r.Id == rotating.Id);
        Assert.False(old.IsActive);
        Assert.Equal(Monday, old.DeactivatedOn);
        Assert.Equal(ScheduleMode.Rotating, old.ScheduleMode);
        Assert.Equal(1, await db.Set<RoutineExerciseLog>().CountAsync(l => l.RoutineExerciseId == lineId));
        Assert.Equal(1, await db.Set<Routine>().CountAsync(r => r.MemberId == w.MemberId && r.IsActive));
    }

    [Fact]
    public async Task Changing_the_mode_of_a_deactivated_routine_is_a_409_and_an_unset_mode_keeps_it()
    {
        var w = await SeedAsync();
        var weekly = await AssignAsync(w, WeeklyRequest(w, (1, "Empuje"), (3, "Tirón")));

        // Sin modo en el body, el PUT edita la rutina semanal y sigue siendo semanal.
        var request = new SaveRoutineRequest("Renombrada", null,
            weekly.Days.Select(d => new RoutineDayInput(
                d.Name, false,
                d.Exercises.Select(e => new RoutineExerciseInput(e.ExerciseId, 3, "10", null, 60, null, e.Id)).ToList(),
                d.Id, d.DayOfWeek)).ToList());
        var edited = await UpdateAsync(w, weekly.Id, request);
        Assert.Equal(weekly.Id, edited.Id);
        Assert.Equal("weekly", edited.ScheduleMode);

        using (var scope = AsStaff(w, Monday))
        {
            await scope.ServiceProvider.GetRequiredService<DeactivateRoutineHandler>().HandleAsync(weekly.Id, default);
        }

        await Assert.ThrowsAsync<ConflictException>(() =>
            UpdateAsync(w, weekly.Id, request with { ScheduleMode = "rotating", Days = [new RoutineDayInput("A", false, [Line(w, 0)])] }));
    }

    // --- edición en el lugar (R1 sigue valiendo) -----------------------------------------------

    [Fact]
    public async Task Editing_a_weekly_routine_keeps_ids_and_logs_even_when_a_day_moves_to_another_weekday()
    {
        var w = await SeedAsync();
        var weekly = await AssignAsync(w, WeeklyRequest(w, (1, "Empuje"), (3, "Tirón"), (5, "Pierna")));
        var monday = weekly.Days[0];
        var wednesday = weekly.Days[1];
        var friday = weekly.Days[2];
        await LogAsync(w, monday.Exercises[0].Id);
        await LogAsync(w, friday.Exercises[0].Id);

        // El lunes pasa al martes, el miércoles se conserva, el viernes se quita y nace un sábado.
        var request = new SaveRoutineRequest("Semanal", null,
        [
            new RoutineDayInput("Empuje", false, KeepLines(monday), monday.Id, 2),
            new RoutineDayInput("Tirón", false, KeepLines(wednesday), wednesday.Id, 3),
            new RoutineDayInput("Nuevo", false, [Line(w, 2)], null, 6),
        ]);
        var updated = await UpdateAsync(w, weekly.Id, request);

        Assert.Equal(weekly.Id, updated.Id);
        Assert.Equal("weekly", updated.ScheduleMode);
        Assert.Equal([2, 3, 6], updated.Days.Select(d => d.DayOfWeek!.Value));
        Assert.Equal(monday.Id, updated.Days[0].Id);
        Assert.Equal(wednesday.Id, updated.Days[1].Id);
        Assert.Equal(monday.Exercises[0].Id, updated.Days[0].Exercises[0].Id);
        Assert.DoesNotContain(updated.Days, d => d.Id == friday.Id);

        using var scope = AsStaff(w, Monday);
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        Assert.Equal(2, await db.Set<RoutineExerciseLog>().CountAsync(l => l.MemberId == w.MemberId));
        Assert.NotNull((await db.Set<RoutineDay>().SingleAsync(d => d.Id == friday.Id)).RetiredOn);

        // El día retirado ya no cuenta para el calendario: su viernes pasa a ser descanso.
        var calendar = await scope.ServiceProvider.GetRequiredService<IRoutineReader>()
            .GetTrainingCalendarAsync(w.MemberId, Day(0), Day(6));
        Assert.Equal([Day(1), Day(2), Day(5)], calendar.TrainingDays.OrderBy(d => d));
        Assert.Contains(Day(4), calendar.RestDays);
    }

    [Fact]
    public async Task Editing_a_weekly_routine_cannot_repeat_a_weekday()
    {
        var w = await SeedAsync();
        var weekly = await AssignAsync(w, WeeklyRequest(w, (1, "Empuje"), (3, "Tirón")));

        var request = new SaveRoutineRequest("Semanal", null,
        [
            new RoutineDayInput("Empuje", false, KeepLines(weekly.Days[0]), weekly.Days[0].Id, 3),
            new RoutineDayInput("Tirón", false, KeepLines(weekly.Days[1]), weekly.Days[1].Id, 3),
        ]);
        await Assert.ThrowsAsync<ValidationDomainException>(() => UpdateAsync(w, weekly.Id, request));
    }

    // --- helpers ------------------------------------------------------------------------------

    private static RoutineExerciseInput Line(World w, int catalogIndex)
        => new(w.CatalogIds[catalogIndex], 3, "10", null, 60, null);

    private static List<RoutineExerciseInput> KeepLines(RoutineDayResponse day)
        => day.Exercises.Select(e => new RoutineExerciseInput(e.ExerciseId, 3, "10", null, 60, null, e.Id)).ToList();

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

    private async Task LogAsync(World w, Guid lineId)
    {
        using var scope = AsMember(w, Monday);
        await scope.ServiceProvider.GetRequiredService<LogRoutineExerciseHandler>()
            .HandleAsync(lineId, new LogRoutineExerciseRequest(3, "10", 20m, null), default);
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
            Role = role,
            GymId = gymId,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        };

    private sealed record World(
        Guid GymId, Guid StaffId, Guid MemberId, Guid MemberUserId, IReadOnlyList<Guid> CatalogIds);
}
