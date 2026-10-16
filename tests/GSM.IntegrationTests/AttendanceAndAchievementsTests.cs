using GSM.Application.Achievements;
using GSM.Application.Achievements.Contracts;
using GSM.Application.Attendance;
using GSM.Application.Attendance.Contracts;
using GSM.Application.Gyms;
using GSM.Application.Gyms.Contracts;
using GSM.Application.Identity.Public;
using GSM.Domain.Achievements;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using GSM.Domain.Common.Exceptions;
using Xunit;

namespace GSM.IntegrationTests;

public sealed class AttendanceAndAchievementsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task CreateGym_seeds_the_default_achievements()
    {
        await TruncateAsync();

        Guid gymId;
        using (var scope = System())
        {
            var result = await scope.ServiceProvider.GetRequiredService<CreateGymHandler>()
                .HandleWithOwnerPasswordAsync(new CreateGymRequest(
                    "Seed Gym", $"seed-{Guid.NewGuid():N}"[..18], null, null, null, GymPlan.Basic,
                    new CreateGymOwner("owner@seed.local", "Seed Owner"), TrialDays: 30), "OwnerPass_123", default);
            gymId = result.GymId;
        }

        using (var scope = System())
        {
            var count = await scope.ServiceProvider.GetRequiredService<GsmDbContext>()
                .Set<Achievement>().CountAsync(a => a.GymId == gymId);
            Assert.Equal(8, count); // L2 suma "1 año con nosotros" y "Primer amigo"; L3 suma "Primer récord"
        }
    }

    [Fact]
    public async Task Checkin_is_once_per_day_and_unlocks_achievements_by_threshold()
    {
        var (gymId, staffId, memberId) = await SeedGymWithAchievementsAsync();
        var day1 = new DateOnly(2026, 5, 4);

        // Día 1: primer check-in → 201 + "Primera visita".
        using (var scope = Staff(gymId, staffId, day1))
        {
            var (res, created) = await scope.ServiceProvider.GetRequiredService<CheckInHandler>()
                .HandleAsync(new CheckInRequest(memberId, null, "Cortesía de prueba"), default);

            Assert.True(created);
            Assert.True(res.MembershipExpired); // sin membresía asignada
            Assert.Contains(res.UnlockedAchievements, a => a.Name == "Primera visita");
        }

        // Día 1 otra vez: una sola entrada por día (A1), el segundo intento es 409 y no suma logros.
        await factory.AgeEntriesAsync(memberId);
        using (var scope = Staff(gymId, staffId, day1))
        {
            await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider.GetRequiredService<CheckInHandler>()
                .HandleAsync(new CheckInRequest(memberId, null, "Cortesía de prueba"), default));
        }

        // Días 2 y 3.
        using (var scope = Staff(gymId, staffId, day1.AddDays(1)))
        {
            await scope.ServiceProvider.GetRequiredService<CheckInHandler>()
                .HandleAsync(new CheckInRequest(memberId, null, "Cortesía de prueba"), default);
        }

        using (var scope = Staff(gymId, staffId, day1.AddDays(2)))
        {
            var (res, _) = await scope.ServiceProvider.GetRequiredService<CheckInHandler>()
                .HandleAsync(new CheckInRequest(memberId, null, "Cortesía de prueba"), default);

            Assert.Contains(res.UnlockedAchievements, a => a.Name == "3 visitas");
        }

        // El estado final: 2 logros desbloqueados, 3 visitas.
        using (var scope = Staff(gymId, staffId, day1.AddDays(2)))
        {
            var items = await scope.ServiceProvider.GetRequiredService<GetMemberAchievementsHandler>()
                .HandleAsync(memberId, default);

            Assert.Equal(2, items.Count(i => i.Unlocked));
        }
    }

    private IServiceScope System()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        return scope;
    }

    private IServiceScope Staff(Guid gymId, Guid staffId, DateOnly day)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(staffId, gymId, UserRole.Staff);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(day);
        return scope;
    }

    private async Task TruncateAsync()
    {
        using var scope = System();
        await scope.ServiceProvider.GetRequiredService<GsmDbContext>().Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, members.members, members.member_invitations, memberships.membership_plans, memberships.memberships, payments.payments, attendance.attendances, achievements.achievements, achievements.member_achievements restart identity cascade;");
    }

    private async Task<(Guid GymId, Guid StaffId, Guid MemberId)> SeedGymWithAchievementsAsync()
    {
        await TruncateAsync();

        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var gym = Gym.Create("Ach Gym", $"ach-{Guid.NewGuid():N}"[..18]);
        var staff = new GSM.Infrastructure.Identity.AppUser
        {
            UserName = "staff@ach.local",
            Email = "staff@ach.local",
            NormalizedUserName = "STAFF@ACH.LOCAL",
            NormalizedEmail = "STAFF@ACH.LOCAL",
            EmailConfirmed = true,
            FullName = "Ach Staff",
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        };
        gym.AssignOwner(staff.Id);
        var member = Member.Create(gym.Id, "Ach", "Member", "doc-ach", new DateOnly(2026, 1, 1));

        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        db.Add(staff);
        db.Add(member);
        db.Add(Achievement.Create(gym.Id, "Primera visita", null, null, AchievementConditionType.Visits, 1));
        db.Add(Achievement.Create(gym.Id, "3 visitas", null, null, AchievementConditionType.Visits, 3));
        await db.SaveChangesAsync();

        return (gym.Id, staff.Id, member.Id);
    }
}
