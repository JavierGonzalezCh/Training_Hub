using GSM.Application.Routines;
using GSM.Application.Routines.Abstractions;
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

public sealed class RoutineTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Assigning_a_routine_that_references_another_gyms_exercise_rolls_back()
    {
        var world = await SeedAsync();

        // Rutina inicial válida para el socio de A.
        Guid firstRoutineId;
        using (var scope = Staff(world.GymA, world.StaffA))
        {
            var routine = await scope.ServiceProvider.GetRequiredService<AssignRoutineHandler>()
                .HandleAsync(world.MemberA, OneDay(world.SystemExerciseIds[0]), default);
            firstRoutineId = routine.Id;
        }

        // Ejercicio propio del gym B.
        Guid exerciseOfB;
        using (var scope = Staff(world.GymB, world.StaffB))
        {
            var created = await scope.ServiceProvider.GetRequiredService<CreateExerciseHandler>()
                .HandleAsync(new CreateExerciseRequest(
                    "Exclusivo de B", null, Muscle.Chest, null, null, null, null), default);
            exerciseOfB = created.Id;
        }

        // Staff de A intenta asignar una rutina que referencia el ejercicio de B.
        using (var scope = Staff(world.GymA, world.StaffA))
        {
            await Assert.ThrowsAsync<ValidationDomainException>(() =>
                scope.ServiceProvider.GetRequiredService<AssignRoutineHandler>()
                    .HandleAsync(world.MemberA, OneDay(exerciseOfB), default));
        }

        using (var scope = Staff(world.GymA, world.StaffA))
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            var routines = await db.Set<Routine>().Where(r => r.MemberId == world.MemberA).ToListAsync();

            Assert.Single(routines);
            Assert.Equal(firstRoutineId, routines[0].Id);
            Assert.True(routines[0].IsActive, "la rutina previa debe seguir activa: nada quedó a medias");
        }
    }

    [Fact]
    public async Task Reassigning_deactivates_the_previous_active_routine()
    {
        var world = await SeedAsync();

        using var scope = Staff(world.GymA, world.StaffA);
        var handler = scope.ServiceProvider.GetRequiredService<AssignRoutineHandler>();

        var first = await handler.HandleAsync(world.MemberA, OneDay(world.SystemExerciseIds[0]), default);
        var second = await handler.HandleAsync(world.MemberA, OneDay(world.SystemExerciseIds[1]), default);

        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var routines = await db.Set<Routine>().Where(r => r.MemberId == world.MemberA).ToListAsync();

        Assert.Equal(2, routines.Count);
        Assert.False(routines.Single(r => r.Id == first.Id).IsActive);
        Assert.True(routines.Single(r => r.Id == second.Id).IsActive);
    }

    [Fact]
    public async Task Member_endpoint_is_403_when_show_routines_is_off_but_staff_still_works()
    {
        var world = await SeedAsync();

        using (var scope = Staff(world.GymA, world.StaffA))
        {
            await scope.ServiceProvider.GetRequiredService<AssignRoutineHandler>()
                .HandleAsync(world.MemberA, OneDay(world.SystemExerciseIds[0]), default);
        }

        await SetShowRoutinesAsync(world.GymA, false);

        using (var scope = AsMember(world.GymA, world.MemberUserA))
        {
            await Assert.ThrowsAsync<ForbiddenDomainException>(() =>
                scope.ServiceProvider.GetRequiredService<GetMyRoutineHandler>().HandleAsync(default));
        }

        // El staff sigue operando con el módulo apagado.
        using (var scope = Staff(world.GymA, world.StaffA))
        {
            var list = await scope.ServiceProvider.GetRequiredService<ListMemberRoutinesHandler>()
                .HandleAsync(world.MemberA, default);
            Assert.Single(list);
        }

        await SetShowRoutinesAsync(world.GymA, true);

        using (var scope = AsMember(world.GymA, world.MemberUserA))
        {
            var mine = await scope.ServiceProvider.GetRequiredService<GetMyRoutineHandler>().HandleAsync(default);
            Assert.NotNull(mine.Routine);
            Assert.NotNull(mine.Today);
        }
    }

    [Fact]
    public async Task Member_only_sees_their_own_routine()
    {
        var world = await SeedAsync();

        using (var scope = Staff(world.GymA, world.StaffA))
        {
            var h = scope.ServiceProvider.GetRequiredService<AssignRoutineHandler>();
            await h.HandleAsync(world.MemberA, OneDay(world.SystemExerciseIds[0]), default);
            await h.HandleAsync(world.OtherMemberA, OneDay(world.SystemExerciseIds[1]), default);
        }

        using var scope2 = AsMember(world.GymA, world.MemberUserA);
        var mine = await scope2.ServiceProvider.GetRequiredService<GetMyRoutineHandler>().HandleAsync(default);

        Assert.Equal(world.MemberA, mine.Routine!.MemberId);
    }

    private static SaveRoutineRequest OneDay(Guid exerciseId)
        => new("Full body", null,
        [
            new RoutineDayInput("Día A", false,
            [
                new RoutineExerciseInput(exerciseId, 3, "8-12", null, 90, null),
            ]),
            new RoutineDayInput(null, true, []),
        ]);

    private IServiceScope Staff(Guid gymId, Guid staffId)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(staffId, gymId, UserRole.Staff);
        return scope;
    }

    private IServiceScope AsMember(Guid gymId, Guid userId)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(userId, gymId, UserRole.Member);
        return scope;
    }

    private async Task SetShowRoutinesAsync(Guid gymId, bool value)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "update gyms.gym_feature_flags set show_routines = {0} where gym_id = {1};", value, gymId);
        scope.Dispose();
    }

    private async Task<World> SeedAsync()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, members.members restart identity cascade;");
        await db.Database.ExecuteSqlRawAsync("delete from routines.routines;");
        await db.Database.ExecuteSqlRawAsync("delete from routines.exercises where gym_id is not null;");

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (gymA, staffA, memberA, memberUserA, otherMemberA) = CreateGym(db, $"rt-a-{suffix}", "RT A", withMemberUser: true);
        var (gymB, staffB, _, _, _) = CreateGym(db, $"rt-b-{suffix}", "RT B", withMemberUser: false);
        await db.SaveChangesAsync();

        var systemIds = await db.Set<Exercise>().Where(e => e.GymId == null)
            .OrderBy(e => e.Name).Select(e => e.Id).Take(3).ToListAsync();

        scope.Dispose();
        return new World(gymA, staffA, memberA, memberUserA, otherMemberA, gymB, staffB, systemIds);
    }

    private static (Guid GymId, Guid StaffId, Guid MemberId, Guid MemberUserId, Guid OtherMemberId) CreateGym(
        GsmDbContext db, string slug, string name, bool withMemberUser)
    {
        var gym = Gym.Create(name, slug);
        var staff = MakeUser($"{slug}-staff", UserRole.Staff, gym.Id);
        gym.AssignOwner(staff.Id);

        var member = Member.Create(gym.Id, "Socio", "Uno", $"doc-{slug}-1", new DateOnly(2026, 1, 1));
        var other = Member.Create(gym.Id, "Socio", "Dos", $"doc-{slug}-2", new DateOnly(2026, 1, 1));

        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        db.Add(staff);
        db.Add(member);
        db.Add(other);

        var memberUserId = Guid.Empty;
        if (withMemberUser)
        {
            var memberUser = MakeUser($"{slug}-member", UserRole.Member, gym.Id);
            db.Add(memberUser);
            member.LinkUser(memberUser.Id);
            memberUserId = memberUser.Id;
        }

        return (gym.Id, staff.Id, member.Id, memberUserId, other.Id);
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
        Guid GymA, Guid StaffA, Guid MemberA, Guid MemberUserA, Guid OtherMemberA,
        Guid GymB, Guid StaffB, IReadOnlyList<Guid> SystemExerciseIds);
}
