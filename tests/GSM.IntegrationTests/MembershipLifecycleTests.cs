using GSM.Application.Memberships;
using GSM.Application.Memberships.Contracts;
using GSM.Application.Memberships.Public;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

public sealed class MembershipLifecycleTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Day = new(2026, 3, 10);

    [Fact]
    public async Task A_cancelled_membership_stops_covering_gives_no_grace_and_frees_its_slot()
    {
        var (gymId, staffId, memberId) = await SeedAsync();
        using var scope = Staff(gymId, staffId, Day);
        var sp = scope.ServiceProvider;
        var planId = await CreatePlanAsync(sp);

        var wrong = await sp.GetRequiredService<AssignMembershipHandler>()
            .HandleAsync(memberId, new AssignMembershipRequest(planId, null), default);

        var cancelled = await sp.GetRequiredService<CancelMembershipHandler>()
            .HandleAsync(memberId, wrong.Id, new CancelMembershipRequest("Plan equivocado"), default);

        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal("Plan equivocado", cancelled.CancellationReason);

        var reader = sp.GetRequiredService<IMembershipReader>();
        Assert.False(await reader.IsActiveAsync(memberId));
        Assert.Null(await reader.GetLatestEndDateAsync(memberId)); // sin días de gracia

        // El hueco queda libre: la siguiente asignación arranca hoy, no tras la cancelada.
        var right = await sp.GetRequiredService<AssignMembershipHandler>()
            .HandleAsync(memberId, new AssignMembershipRequest(planId, null), default);
        Assert.Equal(Day, right.StartDate);

        await Assert.ThrowsAsync<ConflictException>(() => sp.GetRequiredService<CancelMembershipHandler>()
            .HandleAsync(memberId, wrong.Id, new CancelMembershipRequest("Otra vez"), default));
        await Assert.ThrowsAsync<NotFoundException>(() => sp.GetRequiredService<CancelMembershipHandler>()
            .HandleAsync(Guid.NewGuid(), right.Id, new CancelMembershipRequest("Otro miembro"), default));
    }

    [Fact]
    public async Task Resuming_extends_the_end_by_the_frozen_days_and_shifts_the_chained_renewal()
    {
        var (gymId, staffId, memberId) = await SeedAsync();
        Guid planId;
        MembershipResponse current;
        MembershipResponse renewal;

        using (var scope = Staff(gymId, staffId, Day))
        {
            var sp = scope.ServiceProvider;
            planId = await CreatePlanAsync(sp);
            var assign = sp.GetRequiredService<AssignMembershipHandler>();
            current = await assign.HandleAsync(memberId, new AssignMembershipRequest(planId, null), default);
            renewal = await assign.HandleAsync(memberId, new AssignMembershipRequest(planId, null), default);

            // No se congela una membresía que aún no empieza.
            await Assert.ThrowsAsync<ValidationDomainException>(() => sp.GetRequiredService<CreateFreezeHandler>()
                .HandleAsync(memberId, renewal.Id, new CreateFreezeRequest(null, null, "Viaje"), default));

            var paused = await sp.GetRequiredService<CreateFreezeHandler>().HandleAsync(memberId, current.Id, new CreateFreezeRequest(null, null, "Viaje"), default);
            Assert.Equal("Paused", paused.Status);
            Assert.Equal(Day, paused.ActiveFreeze!.StartsOn);
            Assert.False(await sp.GetRequiredService<IMembershipReader>().IsActiveAsync(memberId));
        }

        using (var scope = Staff(gymId, staffId, Day.AddDays(7)))
        {
            var sp = scope.ServiceProvider;
            var resumed = await sp.GetRequiredService<ResumeMembershipHandler>().HandleAsync(memberId, current.Id, default);

            Assert.Equal("Active", resumed.Status);
            Assert.Null(resumed.ActiveFreeze);
            Assert.Equal(current.EndDate.AddDays(7), resumed.EndDate);

            var history = await sp.GetRequiredService<ListMemberMembershipsHandler>().HandleAsync(memberId, default);
            var shifted = history.Single(m => m.Id == renewal.Id);
            Assert.Equal(renewal.StartDate.AddDays(7), shifted.StartDate);
            Assert.Equal(renewal.EndDate.AddDays(7), shifted.EndDate);
            Assert.Equal(resumed.EndDate.AddDays(1), shifted.StartDate);

            await Assert.ThrowsAsync<ConflictException>(() => sp.GetRequiredService<ResumeMembershipHandler>()
                .HandleAsync(memberId, current.Id, default));
        }
    }

    private static async Task<Guid> CreatePlanAsync(IServiceProvider sp)
        => (await sp.GetRequiredService<CreatePlanHandler>()
            .HandleAsync(new CreatePlanRequest($"Mensual {Guid.NewGuid():N}"[..20], null, 80000m, 30, null, 30, 1), default)).Id;

    private IServiceScope Staff(Guid gymId, Guid staffId, DateOnly day)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(staffId, gymId, UserRole.Staff);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(day);
        return scope;
    }

    private async Task<(Guid GymId, Guid StaffId, Guid MemberId)> SeedAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, members.members, members.member_invitations, memberships.membership_plans, memberships.memberships restart identity cascade;");

        var gym = Gym.Create("Life Gym", $"life-{Guid.NewGuid():N}"[..20]);
        var staff = new GSM.Infrastructure.Identity.AppUser
        {
            UserName = "staff@life.local",
            Email = "staff@life.local",
            NormalizedUserName = "STAFF@LIFE.LOCAL",
            NormalizedEmail = "STAFF@LIFE.LOCAL",
            EmailConfirmed = true,
            FullName = "Life Staff",
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        };
        gym.AssignOwner(staff.Id);
        var member = Member.Create(gym.Id, "Life", "Member", "doc-life", new DateOnly(2026, 1, 1));

        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        db.Add(staff);
        db.Add(member);
        await db.SaveChangesAsync();

        return (gym.Id, staff.Id, member.Id);
    }
}
