using GSM.Application.Routines;
using GSM.Application.Routines.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Routines;
using GSM.Infrastructure.Migrations;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// R8: fecha de revisión de rutinas. Ajuste por gym (perezoso, 6 semanas por defecto), lista "por revisar"
/// filtrada por entrenador y la acción "marcar revisada".
/// </summary>
public sealed class RoutineReviewTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Day = new(2026, 6, 10);

    [Fact]
    public async Task Assigning_a_routine_sets_the_review_date_six_weeks_out_by_default()
    {
        var w = await SeedAsync();

        using var scope = AsOwner(w);
        var settings = await scope.ServiceProvider.GetRequiredService<GetRoutineSettingsHandler>().HandleAsync(default);
        Assert.Equal(6, settings.ReviewWeeks);

        var routine = await GetRoutineAsync(w, w.MemberWithTrainerRoutineId);
        Assert.Equal(Day.AddDays(42), routine.ReviewOn);
        Assert.Null(routine.LastReviewedAt);
        Assert.Null(routine.LastReviewedBy);
    }

    [Fact]
    public async Task The_gym_setting_changes_the_date_of_routines_assigned_afterwards_and_not_the_existing_ones()
    {
        var w = await SeedAsync();

        using (var scope = AsOwner(w))
        {
            var saved = await scope.ServiceProvider.GetRequiredService<UpdateRoutineSettingsHandler>()
                .HandleAsync(new UpdateRoutineSettingsRequest(2), default);
            Assert.Equal(2, saved.ReviewWeeks);
        }

        // La ya asignada conserva su fecha; la nueva usa las 2 semanas.
        Assert.Equal(Day.AddDays(42), (await GetRoutineAsync(w, w.MemberWithTrainerRoutineId)).ReviewOn);

        Guid fresh;
        using (var scope = AsOwner(w))
        {
            var created = await scope.ServiceProvider.GetRequiredService<AssignRoutineHandler>()
                .HandleAsync(w.MemberWithoutTrainerId, OneDayRequest(w), default);
            fresh = created.Id;
            Assert.Equal(Day.AddDays(14), created.ReviewOn);
        }

        Assert.Equal(Day.AddDays(14), (await GetRoutineAsync(w, fresh)).ReviewOn);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(53)]
    public async Task Review_weeks_outside_one_to_fifty_two_are_rejected(int weeks)
    {
        var w = await SeedAsync();
        using var scope = AsOwner(w);

        await Assert.ThrowsAsync<ValidationDomainException>(() =>
            scope.ServiceProvider.GetRequiredService<UpdateRoutineSettingsHandler>()
                .HandleAsync(new UpdateRoutineSettingsRequest(weeks), default));
    }

    [Fact]
    public async Task The_due_list_includes_overdue_and_the_next_seven_days_but_not_later_ones()
    {
        var w = await SeedAsync();

        // Revisión el 2026-07-22 (hoy 06-10 + 42). 16 días antes aún no entra en la ventana de 7 días.
        Assert.Empty((await DueAsync(w, w.OwnerId, UserRole.Owner, Day.AddDays(20))).Items);

        // A 6 días: entra. Vencida hace 30 días: sigue entrando, la más atrasada primero.
        var near = await DueAsync(w, w.OwnerId, UserRole.Owner, Day.AddDays(36));
        Assert.Equal(2, near.Items.Count);

        var late = await DueAsync(w, w.OwnerId, UserRole.Owner, Day.AddDays(72));
        Assert.Equal(2, late.Items.Count);
        Assert.True(late.Items[0].ReviewOn <= late.Items[1].ReviewOn);
        Assert.All(late.Items, i => Assert.True(i.ReviewOn < Day.AddDays(72)));
        Assert.Contains(late.Items, i => i.MemberName == "Con Entrenador" && i.RoutineName == "Rutina");
    }

    [Fact]
    public async Task A_trainer_sees_only_the_routines_of_their_members_by_default_and_can_ask_for_all()
    {
        var w = await SeedAsync();
        var overdue = Day.AddDays(60);

        var byDefault = await DueAsync(w, w.TrainerId, UserRole.Staff, overdue, mine: null, permissions: [StaffPermission.Trainer]);
        Assert.True(byDefault.Mine);
        Assert.Equal(w.MemberWithTrainerRoutineId, Assert.Single(byDefault.Items).RoutineId);

        var all = await DueAsync(w, w.TrainerId, UserRole.Staff, overdue, mine: false, permissions: [StaffPermission.Trainer]);
        Assert.False(all.Mine);
        Assert.Equal(2, all.Items.Count);
    }

    [Fact]
    public async Task Without_the_trainer_permission_and_the_owner_see_everything_by_default()
    {
        var w = await SeedAsync();
        var overdue = Day.AddDays(60);

        var reception = await DueAsync(w, w.ReceptionId, UserRole.Staff, overdue);
        Assert.False(reception.Mine);
        Assert.Equal(2, reception.Items.Count);

        var owner = await DueAsync(w, w.OwnerId, UserRole.Owner, overdue);
        Assert.False(owner.Mine);
        Assert.Equal(2, owner.Items.Count);

        // Pedir "mis miembros" a alguien sin miembros asignados da una lista vacía, no un error.
        var mineOfReception = await DueAsync(w, w.ReceptionId, UserRole.Staff, overdue, mine: true);
        Assert.True(mineOfReception.Mine);
        Assert.Empty(mineOfReception.Items);
    }

    [Fact]
    public async Task Inactive_members_do_not_show_up_in_the_list()
    {
        var w = await SeedAsync();

        using (var scope = AsOwner(w))
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            (await db.Set<Member>().SingleAsync(m => m.Id == w.MemberWithoutTrainerId)).Deactivate();
            await db.SaveChangesAsync();
        }

        var due = await DueAsync(w, w.OwnerId, UserRole.Owner, Day.AddDays(60));
        Assert.Equal(w.MemberWithTrainerRoutineId, Assert.Single(due.Items).RoutineId);
    }

    [Fact]
    public async Task Marking_a_routine_reviewed_records_who_and_when_and_moves_the_next_review()
    {
        var w = await SeedAsync();
        var reviewDay = Day.AddDays(50);

        RoutineReviewResponse result;
        using (var scope = AsStaffOn(w, w.TrainerId, reviewDay, StaffPermission.Trainer))
        {
            result = await scope.ServiceProvider.GetRequiredService<ReviewRoutineHandler>()
                .HandleAsync(w.MemberWithTrainerRoutineId, default);
        }

        // 6 semanas desde el día de la revisión, no desde la fecha anterior.
        Assert.Equal(reviewDay.AddDays(42), result.ReviewOn);

        var routine = await GetRoutineAsync(w, w.MemberWithTrainerRoutineId);
        Assert.Equal(reviewDay.AddDays(42), routine.ReviewOn);
        Assert.Equal(w.TrainerId, routine.LastReviewedBy);
        Assert.NotNull(routine.LastReviewedAt);
        Assert.True(routine.IsActive);

        // Ya no está por revisar, pero la otra rutina sigue pendiente.
        var due = await DueAsync(w, w.OwnerId, UserRole.Owner, reviewDay);
        Assert.Equal(w.MemberWithoutTrainerRoutineId, Assert.Single(due.Items).RoutineId);
    }

    [Fact]
    public async Task Marking_reviewed_uses_the_weeks_configured_in_the_gym()
    {
        var w = await SeedAsync();
        using (var scope = AsOwner(w))
        {
            await scope.ServiceProvider.GetRequiredService<UpdateRoutineSettingsHandler>()
                .HandleAsync(new UpdateRoutineSettingsRequest(4), default);
        }

        using var staff = AsStaffOn(w, w.ReceptionId, Day.AddDays(10));
        var result = await staff.ServiceProvider.GetRequiredService<ReviewRoutineHandler>()
            .HandleAsync(w.MemberWithTrainerRoutineId, default);

        Assert.Equal(Day.AddDays(10).AddDays(28), result.ReviewOn);
    }

    [Fact]
    public async Task A_deactivated_routine_cannot_be_reviewed_and_an_unknown_one_is_a_404()
    {
        var w = await SeedAsync();

        using (var scope = AsOwner(w))
        {
            await scope.ServiceProvider.GetRequiredService<DeactivateRoutineHandler>()
                .HandleAsync(w.MemberWithTrainerRoutineId, default);
        }

        using var staff = AsStaffOn(w, w.ReceptionId, Day);
        var handler = staff.ServiceProvider.GetRequiredService<ReviewRoutineHandler>();
        await Assert.ThrowsAsync<ConflictException>(() => handler.HandleAsync(w.MemberWithTrainerRoutineId, default));
        await Assert.ThrowsAsync<NotFoundException>(() => handler.HandleAsync(Guid.NewGuid(), default));

        // Una rutina dada de baja tampoco aparece en la lista.
        var due = await DueAsync(w, w.OwnerId, UserRole.Owner, Day.AddDays(60));
        Assert.Equal(w.MemberWithoutTrainerRoutineId, Assert.Single(due.Items).RoutineId);
    }

    [Fact]
    public async Task The_member_sees_the_review_date_of_their_routine()
    {
        var w = await SeedAsync();

        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(w.MemberUserId, w.GymId, UserRole.Member);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Day);

        var mine = await scope.ServiceProvider.GetRequiredService<GetMyRoutineHandler>().HandleAsync(default);

        Assert.Equal(Day.AddDays(42), mine.Routine!.ReviewOn);
    }

    [Fact]
    public async Task The_migration_script_dates_the_existing_routines_six_weeks_after_they_started()
    {
        var w = await SeedAsync();

        using var scope = AsOwner(w);
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        // Simula una rutina anterior a la columna (review_on con el vacío de EF) y corre el script; DDL/DML transaccional: se revierte.
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("update routines.routines set review_on = date '0001-01-01';");
        await db.Database.ExecuteSqlRawAsync(MigrationScripts.Read("R8"));
        var filled = await db.Database
            .SqlQueryRaw<DateOnly>(
                "select review_on as \"Value\" from routines.routines where id = {0}", w.MemberWithTrainerRoutineId)
            .SingleAsync();
        await tx.RollbackAsync();

        Assert.Equal(Day.AddDays(42), filled);
    }

    // --- helpers -------------------------------------------------------------------------------

    private async Task<DueRoutinesResponse> DueAsync(
        World w, Guid userId, UserRole role, DateOnly today, bool? mine = null, StaffPermission[]? permissions = null)
    {
        using var scope = factory.CreateScopeWithMigration();
        var user = scope.ServiceProvider.GetRequiredService<MutableCurrentUser>();
        user.ActAs(userId, w.GymId, role);
        foreach (var permission in permissions ?? [])
        {
            user.Permissions.Add(permission);
        }

        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(today);
        return await scope.ServiceProvider.GetRequiredService<ListRoutinesDueForReviewHandler>().HandleAsync(mine, default);
    }

    private async Task<Routine> GetRoutineAsync(World w, Guid routineId)
    {
        using var scope = AsOwner(w);
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        return await db.Set<Routine>().AsNoTracking().SingleAsync(r => r.Id == routineId);
    }

    private IServiceScope AsOwner(World w) => AsStaffOn(w, w.OwnerId, Day, role: UserRole.Owner);

    private IServiceScope AsStaffOn(
        World w, Guid userId, DateOnly today, StaffPermission? permission = null, UserRole role = UserRole.Staff)
    {
        var scope = factory.CreateScopeWithMigration();
        var user = scope.ServiceProvider.GetRequiredService<MutableCurrentUser>();
        user.ActAs(userId, w.GymId, role);
        if (permission is { } p)
        {
            user.Permissions.Add(p);
        }

        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(today);
        return scope;
    }

    private static SaveRoutineRequest OneDayRequest(World w)
        => new("Rutina", null,
        [
            new RoutineDayInput("D1", false,
                [new RoutineExerciseInput(w.ExerciseId, TestSets.Normal(3, 10), 60, null)]),
        ]);

    private async Task<World> SeedAsync()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, members.members restart identity cascade;");
        await db.Database.ExecuteSqlRawAsync(
            "delete from routines.routine_exercise_logs; delete from routines.routines; delete from routines.routine_settings;");

        var slug = $"r8-{Guid.NewGuid():N}"[..12];
        var gym = Gym.Create("R8 Gym", slug);
        var owner = MakeUser($"{slug}-o");
        var trainer = MakeUser($"{slug}-t");
        var reception = MakeUser($"{slug}-r");
        gym.AssignOwner(owner.Id);

        var memberUser = MakeUser($"{slug}-m");
        var withTrainer = Member.Create(gym.Id, "Con", "Entrenador", $"doc-{slug}-1", new DateOnly(2026, 1, 1));
        withTrainer.LinkUser(memberUser.Id);
        withTrainer.AssignTrainer(trainer.Id);
        var withoutTrainer = Member.Create(gym.Id, "Sin", "Entrenador", $"doc-{slug}-2", new DateOnly(2026, 1, 1));

        db.AddRange(gym, GymFeatureFlags.Default(gym.Id), owner, trainer, reception, memberUser, withTrainer, withoutTrainer);
        await db.SaveChangesAsync();

        var exerciseId = await db.Set<Exercise>().Where(e => e.GymId == null)
            .OrderBy(e => e.Name).Select(e => e.Id).FirstAsync();

        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(owner.Id, gym.Id, UserRole.Owner);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Day);
        var assign = scope.ServiceProvider.GetRequiredService<AssignRoutineHandler>();
        var world0 = new World(gym.Id, owner.Id, trainer.Id, reception.Id, memberUser.Id,
            withTrainer.Id, withoutTrainer.Id, Guid.Empty, Guid.Empty, exerciseId);
        var first = await assign.HandleAsync(withTrainer.Id, OneDayRequest(world0), default);
        var second = await assign.HandleAsync(withoutTrainer.Id, OneDayRequest(world0), default);
        scope.Dispose();

        return world0 with { MemberWithTrainerRoutineId = first.Id, MemberWithoutTrainerRoutineId = second.Id };
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
        Guid GymId, Guid OwnerId, Guid TrainerId, Guid ReceptionId, Guid MemberUserId,
        Guid MemberWithTrainerId, Guid MemberWithoutTrainerId,
        Guid MemberWithTrainerRoutineId, Guid MemberWithoutTrainerRoutineId, Guid ExerciseId);
}
