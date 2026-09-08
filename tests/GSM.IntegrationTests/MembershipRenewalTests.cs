using GSM.Application.Common.Abstractions;
using GSM.Application.Memberships;
using GSM.Application.Memberships.Abstractions;
using GSM.Application.Memberships.Contracts;
using GSM.Application.Memberships.Public;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

public sealed class MembershipRenewalTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Chained_renewal_computes_dates_and_never_leaves_two_covering_today()
    {
        var (gymId, staffId, memberId) = await SeedAsync();

        // El día de negocio sale del reloj de la app (zona del gym), no de UTC: entre las 19:00
        // y medianoche en Colombia la fecha UTC ya es la de mañana.
        DateOnly today;
        using (var clockScope = factory.CreateScopeWithMigration())
        {
            today = clockScope.ServiceProvider.GetRequiredService<IClock>().Today;
        }

        Guid planId;
        MembershipResponse first;
        MembershipResponse second;

        using (var scope = Staff(gymId, staffId))
        {
            var sp = scope.ServiceProvider;
            planId = (await sp.GetRequiredService<CreatePlanHandler>()
                .HandleAsync(new CreatePlanRequest("Mensual", null, 80000m, 30), default)).Id;

            first = await sp.GetRequiredService<AssignMembershipHandler>()
                .HandleAsync(memberId, new AssignMembershipRequest(planId, null), default);

            second = await sp.GetRequiredService<AssignMembershipHandler>()
                .HandleAsync(memberId, new AssignMembershipRequest(planId, null), default);
        }

        // Fechas: 30 días inclusive, la segunda arranca al día siguiente de la primera.
        Assert.Equal(today, first.StartDate);
        Assert.Equal(today.AddDays(29), first.EndDate);
        Assert.Equal(today.AddDays(30), second.StartDate);
        Assert.Equal(today.AddDays(59), second.EndDate);

        using (var scope = Staff(gymId, staffId))
        {
            var reader = scope.ServiceProvider.GetRequiredService<IMembershipReader>();
            var repo = scope.ServiceProvider.GetRequiredService<IMembershipRepository>();

            Assert.True(await reader.IsActiveAsync(memberId));

            var active = await reader.GetActiveAsync(memberId);
            Assert.NotNull(active);
            Assert.Equal(first.Id, active!.MembershipId); // la que cubre hoy

            var history = await repo.GetHistoryAsync(memberId);
            Assert.Equal(2, history.Count);
            var covering = history.Count(m => m.CoversDate(today));
            Assert.Equal(1, covering);
        }
    }

    private IServiceScope Staff(Guid gymId, Guid staffId)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>()
            .ActAs(staffId, gymId, UserRole.Staff);
        return scope;
    }

    private async Task<(Guid GymId, Guid StaffId, Guid MemberId)> SeedAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, members.members, members.member_invitations, memberships.membership_plans, memberships.memberships restart identity cascade;");

        var gym = Gym.Create("Renew Gym", $"renew-{Guid.NewGuid():N}"[..20]);
        var staff = new GSM.Infrastructure.Identity.AppUser
        {
            UserName = "staff@renew.local",
            Email = "staff@renew.local",
            NormalizedUserName = "STAFF@RENEW.LOCAL",
            NormalizedEmail = "STAFF@RENEW.LOCAL",
            EmailConfirmed = true,
            FullName = "Renew Staff",
            Role = UserRole.Staff,
            GymId = gym.Id,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        };
        gym.AssignOwner(staff.Id);
        var member = Member.Create(
            gym.Id, "Renew", "Member", "doc-renew", clock.Today);

        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        db.Add(staff);
        db.Add(member);
        await db.SaveChangesAsync();

        return (gym.Id, staff.Id, member.Id);
    }
}
