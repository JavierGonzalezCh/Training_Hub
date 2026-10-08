using GSM.Application.Routines;
using GSM.Application.Routines.Abstractions;
using GSM.Application.Routines.Contracts;
using GSM.Application.Routines.Public;
using GSM.Domain.Achievements;
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

/// <summary>Gamificación y auto-servicio (etapa 9.6).</summary>
public sealed class RoutineGamificationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Logging_sessions_unlocks_a_routine_sessions_achievement()
    {
        var w = await SeedAsync(selfService: false);
        await AssignRoutineAsync(w, w.StaffId, w.MemberId, selfAssigned: false);

        await LogOnDayAsync(w, new DateOnly(2026, 7, 1));
        await LogOnDayAsync(w, new DateOnly(2026, 7, 2));

        using var scope = AsSystem();
        var unlocked = await scope.ServiceProvider.GetRequiredService<GsmDbContext>()
            .Set<MemberAchievement>().CountAsync(m => m.MemberId == w.MemberId);
        Assert.Equal(1, unlocked);
    }

    [Fact]
    public async Task A_self_assigned_session_does_not_count_nor_unlock()
    {
        var w = await SeedAsync(selfService: true);
        await SelfAssignRoutineAsync(w);

        await LogOnDayAsync(w, new DateOnly(2026, 7, 1));
        await LogOnDayAsync(w, new DateOnly(2026, 7, 2));
        await LogOnDayAsync(w, new DateOnly(2026, 7, 3));

        using var scope = AsSystem();
        var routines = scope.ServiceProvider.GetRequiredService<IRoutineReader>();
        Assert.Equal(0, await routines.GetCompletedSessionCountAsync(w.MemberId, onlyAssigned: true));
        Assert.Equal(3, await routines.GetCompletedSessionCountAsync(w.MemberId, onlyAssigned: false));

        var unlocked = await scope.ServiceProvider.GetRequiredService<GsmDbContext>()
            .Set<MemberAchievement>().CountAsync(m => m.MemberId == w.MemberId);
        Assert.Equal(0, unlocked);
    }

    [Fact]
    public async Task Self_service_is_403_when_the_flag_is_off_and_works_when_on()
    {
        var w = await SeedAsync(selfService: false);

        using (var scope = AsMember(w))
        {
            await Assert.ThrowsAsync<ForbiddenDomainException>(() =>
                scope.ServiceProvider.GetRequiredService<SelfAssignRoutineHandler>()
                    .HandleAsync(Routine(w), default));
        }

        await EnableSelfServiceAsync(w);

        using (var scope = AsMember(w))
        {
            var routine = await scope.ServiceProvider.GetRequiredService<SelfAssignRoutineHandler>()
                .HandleAsync(Routine(w), default);
            Assert.True(routine.SelfAssigned);
        }
    }

    private async Task AssignRoutineAsync(World w, Guid staffId, Guid memberId, bool selfAssigned)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(staffId, w.GymId, UserRole.Staff);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(new DateOnly(2026, 6, 30));
        await scope.ServiceProvider.GetRequiredService<AssignRoutineHandler>()
            .HandleAsync(memberId, Routine(w), default);
    }

    private async Task SelfAssignRoutineAsync(World w)
    {
        using var scope = AsMember(w);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(new DateOnly(2026, 6, 30));
        await scope.ServiceProvider.GetRequiredService<SelfAssignRoutineHandler>().HandleAsync(Routine(w), default);
    }

    private async Task LogOnDayAsync(World w, DateOnly day)
    {
        using var scope = AsMember(w);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(day);

        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var lineId = await db.Set<RoutineExercise>()
            .Where(e => db.Set<RoutineDay>().Any(d => d.Id == e.RoutineDayId && !d.IsRest))
            .Select(e => e.Id).FirstAsync();

        await scope.ServiceProvider.GetRequiredService<LogRoutineExerciseHandler>()
            .HandleAsync(lineId, new LogRoutineExerciseRequest(null, null, null, null), default);
    }

    private static SaveRoutineRequest Routine(World w)
        => new("Rutina", null,
        [
            new RoutineDayInput("D1", false, [new RoutineExerciseInput(w.ExerciseId, 3, "10", null, 60, null)]),
            new RoutineDayInput(null, true, []),
        ]);

    private IServiceScope AsSystem()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        return scope;
    }

    private IServiceScope AsMember(World w)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(w.MemberUserId, w.GymId, UserRole.Member);
        return scope;
    }

    private async Task EnableSelfServiceAsync(World w)
    {
        using var scope = AsSystem();
        await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Database.ExecuteSqlRawAsync(
            "update gyms.gym_feature_flags set members_can_create_routines = true where gym_id = {0};", w.GymId);
    }

    private async Task<World> SeedAsync(bool selfService)
    {
        var scope = AsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, members.members, attendance.attendances, achievements.achievements, achievements.member_achievements, achievements.streak_freezes restart identity cascade;");
        await db.Database.ExecuteSqlRawAsync("delete from routines.routine_exercise_logs; delete from routines.routines;");

        var slug = $"gam-{Guid.NewGuid():N}"[..12];
        var gym = Gym.Create("Gam Gym", slug);
        var staff = MakeUser($"{slug}-s", UserRole.Staff, gym.Id);
        gym.AssignOwner(staff.Id);
        var memberUser = MakeUser($"{slug}-m", UserRole.Member, gym.Id);
        var member = Member.Create(gym.Id, "Socio", "Gam", $"doc-{slug}", new DateOnly(2026, 1, 1));
        member.LinkUser(memberUser.Id);

        var flags = GymFeatureFlags.Default(gym.Id);
        if (selfService)
        {
            flags.Update(true, true, true, true, true, true, true, membersCanCreateRoutines: true);
        }

        db.AddRange(gym, flags, staff, memberUser, member,
            Achievement.Create(gym.Id, "2 sesiones de rutina", null, null, AchievementConditionType.RoutineSessions, 2));
        await db.SaveChangesAsync();

        var exerciseId = await db.Set<Exercise>().Where(e => e.GymId == null).Select(e => e.Id).FirstAsync();
        scope.Dispose();
        return new World(gym.Id, staff.Id, member.Id, memberUser.Id, exerciseId);
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

    private sealed record World(Guid GymId, Guid StaffId, Guid MemberId, Guid MemberUserId, Guid ExerciseId);
}
