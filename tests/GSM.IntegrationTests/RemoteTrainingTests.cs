using GSM.Application.Attendance;
using GSM.Application.Attendance.Contracts;
using GSM.Application.Attendance.Public;
using GSM.Application.Routines;
using GSM.Application.Routines.Contracts;
using GSM.Domain.Attendance;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Memberships;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// T4 — <c>RemoteTraining</c> (entrenar fuera de la sede) y <c>TrainingEntryTrigger</c> (el día cuenta al iniciar o al
/// registrar el primer ejercicio), sobre el conteo por entrenamiento de A8.
/// </summary>
public sealed class RemoteTrainingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = new(2026, 10, 5);

    // ---- RemoteTraining ----

    [Fact]
    public async Task A_closed_gym_blocks_the_training_count_unless_the_members_train_remotely()
    {
        var local = await SeedAsync(remote: false, closedToday: true);
        var ex = await Assert.ThrowsAsync<ConflictException>(() => StartAsync(local));
        Assert.Contains("cerrado", ex.Message, StringComparison.OrdinalIgnoreCase);

        var remote = await SeedAsync(remote: true, closedToday: true);
        var (response, created) = await StartAsync(remote);
        Assert.True(created);
        Assert.Equal("Routine", response.Method);
    }

    [Fact]
    public async Task Reception_still_respects_the_opening_hours_with_remote_training_on()
    {
        var w = await SeedAsync(remote: true, closedToday: true);

        using var s = Scope(w, UserRole.Staff);
        await Assert.ThrowsAsync<ConflictException>(() => s.ServiceProvider.GetRequiredService<CheckInHandler>()
            .HandleAsync(new CheckInRequest(w.MemberId, null), default));
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public async Task Remote_training_entries_do_not_count_for_occupancy_or_peak_hours(bool remote, int expected)
    {
        var w = await SeedAsync(remote, closedToday: false);
        await StartAsync(w);

        using var s = Scope(w, UserRole.Staff);
        var reader = s.ServiceProvider.GetRequiredService<IAttendanceReader>();
        Assert.Equal(expected, (await reader.GetOccupancyAsync(w.GymId)).PeopleInside);
        Assert.Equal(expected, (await reader.GetHourlyCountsAsync(1)).Sum(h => h.Count));
    }

    [Fact]
    public async Task The_settings_round_trip_the_two_new_options_and_keep_them_when_omitted()
    {
        var w = await SeedAsync(remote: false, closedToday: false);

        using var s = Scope(w, UserRole.Owner);
        var update = s.ServiceProvider.GetRequiredService<UpdateCheckinSettingsHandler>();

        var saved = await update.HandleAsync(
            new UpdateCheckinSettingsRequest("Rotating", RemoteTraining: true, TrainingEntryTrigger: "FirstLog"), default);
        Assert.True(saved.RemoteTraining);
        Assert.Equal("FirstLog", saved.TrainingEntryTrigger);

        var kept = await update.HandleAsync(new UpdateCheckinSettingsRequest("Rotating"), default);
        Assert.True(kept.RemoteTraining);
        Assert.Equal("FirstLog", kept.TrainingEntryTrigger);

        await Assert.ThrowsAsync<ValidationDomainException>(() => update.HandleAsync(
            new UpdateCheckinSettingsRequest("Rotating", TrainingEntryTrigger: "whenever"), default));
    }

    // ---- first_log ----

    [Fact]
    public async Task With_first_log_the_button_is_refused_and_the_first_exercise_counts_the_day_once()
    {
        var w = await SeedAsync(remote: true, closedToday: false, trigger: TrainingEntryTrigger.FirstLog, withRoutine: true);

        var ex = await Assert.ThrowsAsync<ForbiddenDomainException>(() => StartAsync(w));
        Assert.Contains("primer ejercicio", ex.Message);
        Assert.Equal(0, await EntryCountAsync(w));

        await LogAsync(w, w.Lines[0]);
        Assert.Equal(1, await EntryCountAsync(w));

        // El segundo ejercicio del día no suma otra visita.
        await LogAsync(w, w.Lines[1]);
        Assert.Equal(1, await EntryCountAsync(w));

        using var s = Scope(w, UserRole.Member);
        var saved = await s.ServiceProvider.GetRequiredService<GsmDbContext>()
            .Set<GSM.Domain.Attendance.Attendance>().SingleAsync(a => a.MemberId == w.MemberId);
        Assert.Equal(AttendanceMethod.Routine, saved.Method);
        Assert.True(await s.ServiceProvider.GetRequiredService<IAttendanceReader>().IsFirstLogEntryAsync(w.GymId));
    }

    [Fact]
    public async Task With_start_the_first_exercise_does_not_create_an_entry_by_itself()
    {
        var w = await SeedAsync(remote: false, closedToday: false, trigger: TrainingEntryTrigger.Start, withRoutine: true);

        await LogAsync(w, w.Lines[0]);

        Assert.Equal(0, await EntryCountAsync(w));
    }

    [Fact]
    public async Task With_first_log_and_an_expired_membership_the_exercise_is_saved_and_the_day_does_not_count()
    {
        var w = await SeedAsync(
            remote: true, closedToday: false, trigger: TrainingEntryTrigger.FirstLog, withRoutine: true, endedDaysAgo: 10);

        await LogAsync(w, w.Lines[0]);

        using var s = Scope(w, UserRole.Member);
        var db = s.ServiceProvider.GetRequiredService<GsmDbContext>();
        Assert.Equal(0, await EntryCountAsync(w));
        Assert.True(await db.Set<GSM.Domain.Routines.RoutineExerciseLog>().AnyAsync(l => l.MemberId == w.MemberId));
        // El rechazo queda como intento, igual que con el QR.
        Assert.True(await db.Set<CheckinAttempt>().AnyAsync(a => a.MemberId == w.MemberId));
    }

    [Fact]
    public async Task A_rejected_first_exercise_is_retried_by_the_next_one_once_the_membership_is_renewed()
    {
        var w = await SeedAsync(
            remote: true, closedToday: false, trigger: TrainingEntryTrigger.FirstLog, withRoutine: true, endedDaysAgo: 10);

        await LogAsync(w, w.Lines[0]);
        Assert.Equal(0, await EntryCountAsync(w));

        // Recepción renueva la membresía y el miembro sigue entrenando ese mismo día.
        using (var scope = factory.CreateScopeWithMigration())
        {
            scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            var plan = await db.Set<MembershipPlan>().IgnoreQueryFilters().FirstAsync(p => p.GymId == w.GymId);
            db.Add(Membership.Create(w.GymId, w.MemberId, plan.Id, Today.AddDays(-1), 30));
            await db.SaveChangesAsync();
        }

        await LogAsync(w, w.Lines[1]);
        Assert.Equal(1, await EntryCountAsync(w));

        // Un tercer ejercicio no suma otra visita.
        using (var scope = Scope(w, UserRole.Member))
        {
            var logs = scope.ServiceProvider.GetRequiredService<GsmDbContext>().Set<GSM.Domain.Routines.RoutineExerciseLog>();
            Assert.Equal(2, await logs.CountAsync(l => l.MemberId == w.MemberId));
        }
    }

    [Fact]
    public void The_personal_trainer_template_turns_both_options_on()
    {
        var template = CheckinTemplates.For(GymType.PersonalTrainer);

        Assert.True(template.RemoteTraining);
        Assert.Equal(TrainingEntryTrigger.FirstLog, template.TrainingEntryTrigger);
        foreach (var type in new[] { GymType.Gym, GymType.Box, GymType.Studio, GymType.Academy, GymType.Other })
        {
            Assert.False(CheckinTemplates.For(type).RemoteTraining);
            Assert.Equal(TrainingEntryTrigger.Start, CheckinTemplates.For(type).TrainingEntryTrigger);
        }
    }

    // ---- ayudas ----

    private IServiceScope Scope(World w, UserRole role)
    {
        var scope = factory.CreateScopeWithMigration();
        var user = scope.ServiceProvider.GetRequiredService<MutableCurrentUser>();
        user.ActAs(role == UserRole.Member ? w.MemberUserId : Guid.CreateVersion7(), w.GymId, role);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Today);
        return scope;
    }

    private async Task<(CheckInResponse Response, bool Created)> StartAsync(World w)
    {
        using var s = Scope(w, UserRole.Member);
        return await s.ServiceProvider.GetRequiredService<CheckInHandler>().HandleRoutineStartAsync(default);
    }

    private async Task LogAsync(World w, Guid routineExerciseId)
    {
        using var s = Scope(w, UserRole.Member);
        await s.ServiceProvider.GetRequiredService<LogRoutineExerciseHandler>()
            .HandleAsync(routineExerciseId, new LogRoutineExerciseRequest(null), default);
    }

    private async Task<int> EntryCountAsync(World w)
    {
        using var s = Scope(w, UserRole.Member);
        return await s.ServiceProvider.GetRequiredService<GsmDbContext>()
            .Set<GSM.Domain.Attendance.Attendance>().CountAsync(a => a.MemberId == w.MemberId);
    }

    private async Task<World> SeedAsync(
        bool remote,
        bool closedToday,
        TrainingEntryTrigger trigger = TrainingEntryTrigger.Start,
        bool withRoutine = false,
        int endedDaysAgo = -10)
    {
        Guid gymId;
        Guid memberId;
        var userId = Guid.CreateVersion7();
        using (var scope = factory.CreateScopeWithMigration())
        {
            scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

            var gym = Gym.Create("Remote Gym", $"rt-{Guid.NewGuid():N}"[..16]);
            var flags = GymFeatureFlags.Default(gym.Id);
            flags.Update(true, true, true, true, true, false, false);

            var settings = CheckinSettings.Default(gym.Id);
            settings.Update(CheckinMode.Rotating, false, remoteTraining: remote, trainingEntryTrigger: trigger);

            var member = Member.Create(gym.Id, "Ana", "Prueba", $"doc-{Guid.NewGuid():N}"[..12], Today.AddDays(-100));
            member.LinkUser(userId);

            var plan = MembershipPlan.Create(gym.Id, "Mensual", null, 50000m, 30);
            var end = Today.AddDays(-endedDaysAgo);
            db.AddRange(gym, flags, settings, member, plan, Membership.Create(gym.Id, member.Id, plan.Id, end.AddDays(-29), 30));
            if (closedToday)
            {
                // Sin horario semanal el gym está siempre abierto: se carga uno y se cierra el día de hoy.
                foreach (var day in Enum.GetValues<OpeningDay>().Where(d => d != OpeningDay.Holiday))
                {
                    db.Add(GymOpeningSlot.Create(gym.Id, day, new TimeSlot(new TimeOnly(0, 0), new TimeOnly(23, 59))));
                }

                db.Add(GymSpecialDay.Create(gym.Id, Today, true, [], "Cerrado"));
            }

            await db.SaveChangesAsync();
            gymId = gym.Id;
            memberId = member.Id;
        }

        var world = new World(gymId, memberId, userId, []);
        if (!withRoutine)
        {
            return world;
        }

        using var staff = Scope(world, UserRole.Staff);
        var catalog = await staff.ServiceProvider.GetRequiredService<GsmDbContext>()
            .Set<GSM.Domain.Routines.Exercise>().Where(e => e.GymId == null)
            .OrderBy(e => e.Name).Select(e => e.Id).Take(2).ToListAsync();
        var routine = await staff.ServiceProvider.GetRequiredService<AssignRoutineHandler>().HandleAsync(
            memberId,
            new SaveRoutineRequest("Diaria", null,
            [
                new RoutineDayInput("A", false,
                [
                    new RoutineExerciseInput(catalog[0], TestSets.Normal(3, 10), 60, null),
                    new RoutineExerciseInput(catalog[1], TestSets.Normal(3, 10), 60, null),
                ], null, null),
            ]),
            default);
        return world with { Lines = routine.Days[0].Exercises.Select(e => e.Id).ToList() };
    }

    private sealed record World(Guid GymId, Guid MemberId, Guid MemberUserId, IReadOnlyList<Guid> Lines);
}
