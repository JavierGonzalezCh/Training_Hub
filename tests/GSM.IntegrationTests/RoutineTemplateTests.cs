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

/// <summary>R11: plantillas de rutina del gym. Se copian al asignar; editarlas o borrarlas no toca lo ya asignado.</summary>
public sealed class RoutineTemplateTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Day = new(2026, 6, 10);

    [Fact]
    public async Task A_template_is_created_listed_and_read_back_with_its_days_and_series()
    {
        var w = await SeedAsync();

        Guid id;
        using (var scope = AsOwner(w.Gym))
        {
            var created = await scope.ServiceProvider.GetRequiredService<CreateRoutineTemplateHandler>()
                .HandleAsync(new SaveRoutineTemplateRequest("Fuerza 3 días", "Para principiantes", TwoDayRequest(w)), default);
            id = created.Id;
            Assert.Equal("rotating", created.ScheduleMode);
            Assert.Equal(2, created.DayCount);
            Assert.Equal(2, created.ExerciseCount);
        }

        using var read = AsOwner(w.Gym);
        var list = await read.ServiceProvider.GetRequiredService<ListRoutineTemplatesHandler>().HandleAsync(default);
        Assert.Equal(id, Assert.Single(list).Id);

        var detail = await read.ServiceProvider.GetRequiredService<GetRoutineTemplateHandler>().HandleAsync(id, default);
        Assert.Equal("Para principiantes", detail.Description);
        Assert.Equal(0, detail.UnavailableExerciseCount);
        Assert.Equal(2, detail.Routine.Days.Count);
        Assert.Equal(3, detail.Routine.Days[0].Exercises[0].SetTargets.Count);
        Assert.NotEqual("?", detail.Routine.Days[0].Exercises[0].ExerciseName);
    }

    [Fact]
    public async Task Assigning_a_template_creates_an_independent_routine_and_replaces_the_active_one()
    {
        var w = await SeedAsync();
        var templateId = await CreateTemplateAsync(w, "Plantilla A");

        Guid first;
        using (var scope = AsOwner(w.Gym))
        {
            first = (await scope.ServiceProvider.GetRequiredService<AssignRoutineHandler>()
                .HandleAsync(w.MemberId, OneDayRequest(w), default)).Id;
        }

        Guid assigned;
        using (var scope = AsOwner(w.Gym))
        {
            var routine = await scope.ServiceProvider.GetRequiredService<AssignRoutineFromTemplateHandler>()
                .HandleAsync(w.MemberId, templateId, default);
            assigned = routine.Id;
            Assert.Equal("Rutina de la plantilla", routine.Name);
            Assert.Equal(2, routine.CycleLength);
            Assert.True(routine.IsActive);
            Assert.False(routine.SelfAssigned);
        }

        using var check = AsOwner(w.Gym);
        var db = check.ServiceProvider.GetRequiredService<GsmDbContext>();
        Assert.False((await db.Set<Routine>().AsNoTracking().SingleAsync(r => r.Id == first)).IsActive);
        Assert.True((await db.Set<Routine>().AsNoTracking().SingleAsync(r => r.Id == assigned)).IsActive);
    }

    [Fact]
    public async Task Editing_or_deleting_a_template_does_not_change_the_routines_already_assigned()
    {
        var w = await SeedAsync();
        var templateId = await CreateTemplateAsync(w, "Plantilla B");

        Guid assigned;
        using (var scope = AsOwner(w.Gym))
        {
            assigned = (await scope.ServiceProvider.GetRequiredService<AssignRoutineFromTemplateHandler>()
                .HandleAsync(w.MemberId, templateId, default)).Id;

            await scope.ServiceProvider.GetRequiredService<UpdateRoutineTemplateHandler>().HandleAsync(
                templateId, new SaveRoutineTemplateRequest("Plantilla B v2", null, OneDayRequest(w)), default);
        }

        using (var scope = AsOwner(w.Gym))
        {
            var routine = await scope.ServiceProvider.GetRequiredService<GetRoutineHandler>().HandleAsync(assigned, default);
            Assert.Equal(2, routine.Days.Count);

            var template = await scope.ServiceProvider.GetRequiredService<GetRoutineTemplateHandler>()
                .HandleAsync(templateId, default);
            Assert.Equal("Plantilla B v2", template.Name);
            Assert.Single(template.Routine.Days);

            await scope.ServiceProvider.GetRequiredService<DeleteRoutineTemplateHandler>().HandleAsync(templateId, default);
        }

        using var after = AsOwner(w.Gym);
        Assert.Equal(2, (await after.ServiceProvider.GetRequiredService<GetRoutineHandler>()
            .HandleAsync(assigned, default)).Days.Count);
        await Assert.ThrowsAsync<NotFoundException>(() =>
            after.ServiceProvider.GetRequiredService<GetRoutineTemplateHandler>().HandleAsync(templateId, default));
    }

    [Fact]
    public async Task A_routine_can_be_saved_as_a_template_and_reassigned()
    {
        var w = await SeedAsync();

        Guid routineId;
        using (var scope = AsOwner(w.Gym))
        {
            routineId = (await scope.ServiceProvider.GetRequiredService<AssignRoutineHandler>()
                .HandleAsync(w.MemberId, TwoDayRequest(w), default)).Id;
        }

        Guid templateId;
        using (var scope = AsOwner(w.Gym))
        {
            var saved = await scope.ServiceProvider.GetRequiredService<SaveRoutineAsTemplateHandler>()
                .HandleAsync(routineId, new SaveRoutineAsTemplateRequest("Desde rutina", null), default);
            templateId = saved.Id;
            Assert.Equal(2, saved.DayCount);
        }

        using var again = AsOwner(w.Gym);
        var copy = await again.ServiceProvider.GetRequiredService<AssignRoutineFromTemplateHandler>()
            .HandleAsync(w.MemberId, templateId, default);
        Assert.NotEqual(routineId, copy.Id);
        Assert.Equal(2, copy.Days.Count);
        Assert.Equal(3, copy.Days[0].Exercises[0].SetTargets.Count);
    }

    [Fact]
    public async Task A_weekly_template_keeps_its_calendar_mode()
    {
        var w = await SeedAsync();

        var request = new SaveRoutineRequest("Semanal", null,
        [
            new RoutineDayInput("Lunes", false,
                [new RoutineExerciseInput(w.ExerciseId, TestSets.Normal(3, 10), 60, null)], null, 1),
            new RoutineDayInput("Jueves", false,
                [new RoutineExerciseInput(w.ExerciseId, TestSets.Normal(3, 10), 60, null)], null, 4),
        ], "weekly");

        using var scope = AsOwner(w.Gym);
        var created = await scope.ServiceProvider.GetRequiredService<CreateRoutineTemplateHandler>()
            .HandleAsync(new SaveRoutineTemplateRequest("Semanal 2 días", null, request), default);
        Assert.Equal("weekly", created.ScheduleMode);

        var routine = await scope.ServiceProvider.GetRequiredService<AssignRoutineFromTemplateHandler>()
            .HandleAsync(w.MemberId, created.Id, default);
        Assert.Equal("weekly", routine.ScheduleMode);
        Assert.Equal([1, 4], routine.Days.Select(d => d.DayOfWeek!.Value).ToArray());
    }

    [Fact]
    public async Task A_template_with_an_unknown_exercise_is_rejected()
    {
        var w = await SeedAsync();
        var request = new SaveRoutineRequest("Mala", null,
        [
            new RoutineDayInput("D1", false,
                [new RoutineExerciseInput(Guid.CreateVersion7(), TestSets.Normal(3, 10), 60, null)]),
        ]);

        using var scope = AsOwner(w.Gym);
        await Assert.ThrowsAsync<ValidationDomainException>(() =>
            scope.ServiceProvider.GetRequiredService<CreateRoutineTemplateHandler>()
                .HandleAsync(new SaveRoutineTemplateRequest("Mala", null, request), default));
    }

    [Fact]
    public async Task Templates_are_private_to_their_gym()
    {
        var w = await SeedAsync();
        var templateId = await CreateTemplateAsync(w, "Solo mía");

        using var other = AsOwner(w.OtherGym);
        Assert.Empty(await other.ServiceProvider.GetRequiredService<ListRoutineTemplatesHandler>().HandleAsync(default));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            other.ServiceProvider.GetRequiredService<GetRoutineTemplateHandler>().HandleAsync(templateId, default));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            other.ServiceProvider.GetRequiredService<DeleteRoutineTemplateHandler>().HandleAsync(templateId, default));
    }

    [Fact]
    public async Task Assigning_to_a_missing_member_or_template_is_404()
    {
        var w = await SeedAsync();
        var templateId = await CreateTemplateAsync(w, "Existe");

        using var scope = AsOwner(w.Gym);
        var handler = scope.ServiceProvider.GetRequiredService<AssignRoutineFromTemplateHandler>();
        await Assert.ThrowsAsync<NotFoundException>(() => handler.HandleAsync(Guid.CreateVersion7(), templateId, default));
        await Assert.ThrowsAsync<NotFoundException>(() => handler.HandleAsync(w.MemberId, Guid.CreateVersion7(), default));
    }

    // ----------------------------------------------------------------------------------------------------

    private async Task<Guid> CreateTemplateAsync(World w, string name)
    {
        using var scope = AsOwner(w.Gym);
        return (await scope.ServiceProvider.GetRequiredService<CreateRoutineTemplateHandler>()
            .HandleAsync(new SaveRoutineTemplateRequest(name, null, TwoDayRequest(w)), default)).Id;
    }

    private IServiceScope AsOwner((Guid GymId, Guid OwnerId) gym)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(gym.OwnerId, gym.GymId, UserRole.Owner);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Day);
        return scope;
    }

    private static SaveRoutineRequest OneDayRequest(World w)
        => new("Rutina", null,
        [
            new RoutineDayInput("D1", false, [new RoutineExerciseInput(w.ExerciseId, TestSets.Normal(3, 10), 60, null)]),
        ]);

    private static SaveRoutineRequest TwoDayRequest(World w)
        => new("Rutina de la plantilla", "Notas",
        [
            new RoutineDayInput("D1", false, [new RoutineExerciseInput(w.ExerciseId, TestSets.Normal(3, 10), 60, null)]),
            new RoutineDayInput("D2", false, [new RoutineExerciseInput(w.ExerciseId, TestSets.Normal(4, 8), 90, null)]),
        ]);

    private async Task<World> SeedAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, members.members restart identity cascade;");
        await db.Database.ExecuteSqlRawAsync(
            "delete from routines.routine_exercise_logs; delete from routines.routines; delete from routines.routine_templates;");

        var slug = $"r11-{Guid.NewGuid():N}"[..12];
        var gym = Gym.Create("R11 Gym", slug);
        var owner = MakeUser($"{slug}-o");
        gym.AssignOwner(owner.Id);
        var otherGym = Gym.Create("R11 Otro", $"{slug}x");
        var otherOwner = MakeUser($"{slug}-p");
        otherGym.AssignOwner(otherOwner.Id);
        var member = Member.Create(gym.Id, "Mi", "Embro", $"doc-{slug}", new DateOnly(2026, 1, 1));

        db.AddRange(
            gym, GymFeatureFlags.Default(gym.Id), owner, otherGym, GymFeatureFlags.Default(otherGym.Id), otherOwner, member);
        await db.SaveChangesAsync();

        var exerciseId = await db.Set<Exercise>().Where(e => e.GymId == null)
            .OrderBy(e => e.Name).Select(e => e.Id).FirstAsync();

        return new World((gym.Id, owner.Id), (otherGym.Id, otherOwner.Id), member.Id, exerciseId);
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
        (Guid GymId, Guid OwnerId) Gym, (Guid GymId, Guid OwnerId) OtherGym, Guid MemberId, Guid ExerciseId);
}
