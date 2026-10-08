using GSM.Application.Common.Abstractions;
using GSM.Domain.Achievements;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Memberships;
using GSM.Domain.Payments;
using AttendanceEntity = GSM.Domain.Attendance.Attendance;
using AttendanceMethodEnum = GSM.Domain.Attendance.AttendanceMethod;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// Prueba crítica de tenancy (plan, etapa 2). Dos gyms con datos; autenticado como owner del gym A,
/// una consulta directa nunca devuelve nada del gym B. Se re-corre en cada etapa siguiente.
/// </summary>
public sealed class TenancyTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public TenancyTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Owner_of_gym_A_cannot_see_gym_B_data()
    {
        var (gymA, ownerA, gymB) = await SeedTwoGymsAsync();

        using var scope = _factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>()
            .ActAs(ownerA, gymA, UserRole.Owner);
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var visibleGyms = await db.Set<Gym>().Select(g => g.Id).ToListAsync();
        var visibleFlags = await db.Set<GymFeatureFlags>().Select(f => f.GymId).ToListAsync();
        var visibleMemberGyms = await db.Set<Member>().Select(m => m.GymId).Distinct().ToListAsync();
        var visiblePlanGyms = await db.Set<MembershipPlan>().Select(p => p.GymId).Distinct().ToListAsync();
        var visibleMembershipGyms = await db.Set<Membership>().Select(m => m.GymId).Distinct().ToListAsync();
        var visiblePaymentGyms = await db.Set<Payment>().Select(p => p.GymId).Distinct().ToListAsync();
        var visibleAttendanceGyms = await db.Set<AttendanceEntity>().Select(a => a.GymId).Distinct().ToListAsync();
        var visibleAchievementGyms = await db.Set<Achievement>().Select(a => a.GymId).Distinct().ToListAsync();
        var canReachB = await db.Set<Gym>().AnyAsync(g => g.Id == gymB);

        Assert.Equal([gymA], visibleGyms);
        Assert.Equal([gymA], visibleFlags);
        Assert.Equal([gymA], visibleMemberGyms);
        Assert.Equal([gymA], visiblePlanGyms);
        Assert.Equal([gymA], visibleMembershipGyms);
        Assert.Equal([gymA], visiblePaymentGyms);
        Assert.Equal([gymA], visibleAttendanceGyms);
        Assert.Equal([gymA], visibleAchievementGyms);
        Assert.False(canReachB);
    }

    [Fact]
    public async Task SuperAdmin_sees_every_gym()
    {
        var (gymA, _, gymB) = await SeedTwoGymsAsync();

        using var scope = _factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>()
            .ActAs(Guid.CreateVersion7(), gymId: null, UserRole.SuperAdmin);
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var visibleGyms = await db.Set<Gym>().Select(g => g.Id).ToListAsync();

        Assert.Contains(gymA, visibleGyms);
        Assert.Contains(gymB, visibleGyms);
    }

    private async Task<(Guid GymA, Guid OwnerA, Guid GymB)> SeedTwoGymsAsync()
    {
        using var scope = _factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, members.members, memberships.membership_plans, memberships.memberships, payments.payments, attendance.attendances, achievements.achievements, achievements.member_achievements restart identity cascade;");

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (gymA, ownerA) = await CreateGymAsync(db, clock, $"gym-a-{suffix}", "Gym A");
        var (gymB, _) = await CreateGymAsync(db, clock, $"gym-b-{suffix}", "Gym B");

        await db.SaveChangesAsync();
        return (gymA, ownerA, gymB);
    }

    private static async Task<(Guid GymId, Guid OwnerId)> CreateGymAsync(
        GsmDbContext db, IClock clock, string slug, string name)
    {
        var gym = Gym.Create(name, slug);
        var owner = new GSM.Infrastructure.Identity.AppUser
        {
            UserName = $"{slug}@test.local",
            Email = $"{slug}@test.local",
            NormalizedUserName = $"{slug}@TEST.LOCAL",
            NormalizedEmail = $"{slug}@TEST.LOCAL",
            EmailConfirmed = true,
            FullName = $"{name} Owner",
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        };

        gym.AssignOwner(owner.Id);
        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        db.Add(owner);

        var member = Member.Create(
            gym.Id, "Test", "Member", $"doc-{slug}", clock.Today);
        var plan = MembershipPlan.Create(gym.Id, "Plan", null, 50000m, 30);
        db.Add(member);
        db.Add(plan);
        db.Add(Membership.Create(
            gym.Id, member.Id, plan.Id, clock.Today, 30));
        db.Add(Payment.Create(
            gym.Id, member.Id, null, 50000m, PaymentMethod.Cash, "Mensualidad", null,
            owner.Id, clock.UtcNow));
        db.Add(AttendanceEntity.Create(
            gym.Id, member.Id, AttendanceMethodEnum.Manual, clock.UtcNow, clock.Today));
        db.Add(Achievement.Create(
            gym.Id, "Primera visita", null, null, AchievementConditionType.Visits, 1));

        await Task.CompletedTask;
        return (gym.Id, owner.Id);
    }
}
