using GSM.Application.Memberships;
using GSM.Application.Memberships.Contracts;
using GSM.Application.Memberships.Public;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Infrastructure.Migrations;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>MS7 — planes de grupo (familiar, pareja, corporativo): una membresía por persona, mismas fechas, juntos en todo.</summary>
public sealed class MembershipGroupTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Day = new(2026, 3, 10);

    [Fact]
    public async Task A_group_plan_has_seats_and_a_pass_cannot_be_a_group()
    {
        var world = await SeedAsync();
        using var scope = Actor(world, Day);
        var sp = scope.ServiceProvider;

        var plan = await CreateGroupPlanAsync(sp, seats: 4);
        Assert.Equal(4, plan.Seats);

        var individual = await sp.GetRequiredService<CreatePlanHandler>().HandleAsync(
            new CreatePlanRequest($"Solo {Guid.NewGuid():N}"[..12], null, 80000m, 30), default);
        Assert.Equal(1, individual.Seats);

        var ex = await Assert.ThrowsAsync<ValidationDomainException>(() => sp.GetRequiredService<CreatePlanHandler>().HandleAsync(
            new CreatePlanRequest($"Tiq {Guid.NewGuid():N}"[..12], null, 90000m, 30, "days", Kind: "pass", Entries: 10, ValidityDays: 30, Seats: 2),
            default));
        Assert.Contains("tiquetera", ex.Message, StringComparison.OrdinalIgnoreCase);

        await Assert.ThrowsAsync<ValidationDomainException>(() => sp.GetRequiredService<CreatePlanHandler>().HandleAsync(
            new CreatePlanRequest($"Gran {Guid.NewGuid():N}"[..12], null, 90000m, 30, Seats: 501), default));
    }

    [Fact]
    public async Task A_group_plan_cannot_be_assigned_as_an_individual_membership()
    {
        var world = await SeedAsync();
        using var scope = Actor(world, Day);
        var sp = scope.ServiceProvider;
        var plan = await CreateGroupPlanAsync(sp, seats: 3);

        await Assert.ThrowsAsync<ValidationDomainException>(() => sp.GetRequiredService<AssignMembershipHandler>()
            .HandleAsync(world.Members[0], new AssignMembershipRequest(plan.Id, null), default));
    }

    [Fact]
    public async Task Assigning_a_group_creates_one_membership_per_person_with_the_same_dates_and_the_holder_pays()
    {
        var world = await SeedAsync();
        using var scope = Actor(world, Day);
        var sp = scope.ServiceProvider;
        var plan = await CreateGroupPlanAsync(sp, seats: 4, price: 200000m);

        var group = await sp.GetRequiredService<AssignGroupMembershipHandler>().HandleAsync(
            new AssignGroupMembershipRequest(world.Members[0], plan.Id, [world.Members[1], world.Members[2]], null), default);

        Assert.Equal(4, group.Seats);
        Assert.Equal(3, group.SeatsUsed);
        Assert.Equal(1, group.SeatsFree);
        Assert.Equal(world.Members[0], group.HolderMemberId);
        Assert.Equal(Day, group.StartDate);
        Assert.Equal(200000m, group.TotalToCharge);
        Assert.Equal(3, group.Members.Count);

        var holder = group.Members.Single(m => m.IsHolder).Membership;
        Assert.Equal(200000m, holder.AgreedPrice);
        Assert.Equal(group.Id, holder.GroupId);
        Assert.True(holder.IsGroupHolder);

        foreach (var beneficiary in group.Members.Where(m => !m.IsHolder))
        {
            Assert.Equal(0m, beneficiary.Membership.AgreedPrice);
            Assert.Equal(0m, beneficiary.Membership.TotalToCharge);
            Assert.Equal(group.Id, beneficiary.Membership.GroupId);
            Assert.False(beneficiary.Membership.IsGroupHolder);
            Assert.Equal(holder.StartDate, beneficiary.Membership.StartDate);
            Assert.Equal(holder.EndDate, beneficiary.Membership.EndDate);
        }

        // Cada beneficiario queda cubierto por su propia membresía.
        var reader = sp.GetRequiredService<IMembershipReader>();
        foreach (var id in world.Members.Take(3))
        {
            var access = await reader.GetAccessStatusAsync(id);
            Assert.True(access.CanEnter);
        }

        Assert.False((await reader.GetAccessStatusAsync(world.Members[3])).CanEnter);

        var fetched = await sp.GetRequiredService<GetMembershipGroupHandler>().HandleAsync(group.Id, default);
        Assert.Equal(group.SeatsUsed, fetched.SeatsUsed);
    }

    [Fact]
    public async Task A_group_validates_the_seats_the_duplicates_and_the_people()
    {
        var world = await SeedAsync();
        using var scope = Actor(world, Day);
        var sp = scope.ServiceProvider;
        var plan = await CreateGroupPlanAsync(sp, seats: 2);
        var assign = sp.GetRequiredService<AssignGroupMembershipHandler>();

        // Un plan de 2 cupos: el titular y uno más. Dos beneficiarios no caben.
        await Assert.ThrowsAsync<ValidationDomainException>(() => assign.HandleAsync(
            new AssignGroupMembershipRequest(world.Members[0], plan.Id, [world.Members[1], world.Members[2]], null), default));

        // El titular no puede ser beneficiario.
        await Assert.ThrowsAsync<ValidationDomainException>(() => assign.HandleAsync(
            new AssignGroupMembershipRequest(world.Members[0], plan.Id, [world.Members[0]], null), default));

        // Un miembro que no existe.
        await Assert.ThrowsAsync<NotFoundException>(() => assign.HandleAsync(
            new AssignGroupMembershipRequest(world.Members[0], plan.Id, [Guid.NewGuid()], null), default));

        // Un plan individual no sirve para un grupo.
        var individual = await sp.GetRequiredService<CreatePlanHandler>().HandleAsync(
            new CreatePlanRequest($"Solo {Guid.NewGuid():N}"[..12], null, 80000m, 30), default);
        await Assert.ThrowsAsync<ValidationDomainException>(() => assign.HandleAsync(
            new AssignGroupMembershipRequest(world.Members[0], individual.Id, [world.Members[1]], null), default));

        // Nada de lo anterior dejó membresías a medias.
        var history = await sp.GetRequiredService<ListMemberMembershipsHandler>().HandleAsync(world.Members[0], default);
        Assert.Empty(history);
    }

    [Fact]
    public async Task A_beneficiary_with_an_overlapping_membership_cannot_join()
    {
        var world = await SeedAsync();
        using var scope = Actor(world, Day);
        var sp = scope.ServiceProvider;
        var group = await CreateGroupPlanAsync(sp, seats: 3);
        var individual = await sp.GetRequiredService<CreatePlanHandler>().HandleAsync(
            new CreatePlanRequest($"Solo {Guid.NewGuid():N}"[..12], null, 80000m, 30), default);
        await sp.GetRequiredService<AssignMembershipHandler>()
            .HandleAsync(world.Members[1], new AssignMembershipRequest(individual.Id, null), default);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => sp.GetRequiredService<AssignGroupMembershipHandler>().HandleAsync(
            new AssignGroupMembershipRequest(world.Members[0], group.Id, [world.Members[1]], null), default));
        Assert.Contains("se cruza", ex.Message);
    }

    [Fact]
    public async Task Free_seats_are_filled_later_with_the_current_dates_until_the_group_is_full()
    {
        var world = await SeedAsync();
        MembershipGroupResponse group;
        using (var scope = Actor(world, Day))
        {
            var sp = scope.ServiceProvider;
            var plan = await CreateGroupPlanAsync(sp, seats: 3);
            group = await sp.GetRequiredService<AssignGroupMembershipHandler>().HandleAsync(
                new AssignGroupMembershipRequest(world.Members[0], plan.Id, [], null), default);
            Assert.Equal(2, group.SeatsFree);
        }

        // Una semana después entra otra persona: hereda el fin del grupo, no una duración nueva.
        using (var scope = Actor(world, Day.AddDays(7)))
        {
            var sp = scope.ServiceProvider;
            var add = sp.GetRequiredService<AddGroupBeneficiaryHandler>();

            var after = await add.HandleAsync(group.Id, new AddGroupBeneficiaryRequest(world.Members[1]), default);
            var joined = after.Members.Single(m => m.MemberId == world.Members[1]).Membership;
            Assert.Equal(Day.AddDays(7), joined.StartDate);
            Assert.Equal(group.EndDate, joined.EndDate);
            Assert.Equal(0m, joined.AgreedPrice);
            Assert.Equal(1, after.SeatsFree);

            // Repetir a la misma persona.
            await Assert.ThrowsAsync<ConflictException>(() =>
                add.HandleAsync(group.Id, new AddGroupBeneficiaryRequest(world.Members[1]), default));

            await add.HandleAsync(group.Id, new AddGroupBeneficiaryRequest(world.Members[2]), default);

            // Lleno.
            var full = await Assert.ThrowsAsync<ConflictException>(() =>
                add.HandleAsync(group.Id, new AddGroupBeneficiaryRequest(world.Members[3]), default));
            Assert.Contains("cupos", full.Message);
        }
    }

    [Fact]
    public async Task Freezing_one_member_freezes_the_whole_group_and_resuming_resumes_it()
    {
        var world = await SeedAsync();
        MembershipGroupResponse group;
        using (var scope = Actor(world, Day))
        {
            var sp = scope.ServiceProvider;
            var plan = await CreateGroupPlanAsync(sp, seats: 3, maxFreeze: 30);
            group = await sp.GetRequiredService<AssignGroupMembershipHandler>().HandleAsync(
                new AssignGroupMembershipRequest(world.Members[0], plan.Id, [world.Members[1], world.Members[2]], null), default);

            // Se congela desde un beneficiario: se congelan los tres.
            var beneficiary = group.Members.First(m => !m.IsHolder);
            await sp.GetRequiredService<CreateFreezeHandler>().HandleAsync(
                beneficiary.MemberId, beneficiary.Membership.Id,
                new CreateFreezeRequest(Day.AddDays(2), Day.AddDays(6), "Viaje familiar"), default);

            var frozen = await sp.GetRequiredService<GetMembershipGroupHandler>().HandleAsync(group.Id, default);
            Assert.All(frozen.Members, m =>
            {
                Assert.Equal(group.EndDate.AddDays(5), m.Membership.EndDate);
                Assert.NotNull(m.Membership.NextFreeze);
            });
            Assert.Equal(group.EndDate.AddDays(5), frozen.EndDate);
        }

        // Dentro del congelamiento, ninguno de los tres entra.
        using (var scope = Actor(world, Day.AddDays(3)))
        {
            var reader = scope.ServiceProvider.GetRequiredService<IMembershipReader>();
            foreach (var id in world.Members.Take(3))
            {
                var access = await reader.GetAccessStatusAsync(id);
                Assert.Equal(CommercialStatus.Frozen, access.Commercial);
                Assert.False(access.CanEnter);
            }
        }

        // Reanudar a la mitad (día 4) desde el titular devuelve los días que ya no se usan a todos.
        using (var scope = Actor(world, Day.AddDays(4)))
        {
            var sp = scope.ServiceProvider;
            await sp.GetRequiredService<ResumeMembershipHandler>().HandleAsync(
                world.Members[0], group.Members.Single(m => m.IsHolder).Membership.Id, default);

            var resumed = await sp.GetRequiredService<GetMembershipGroupHandler>().HandleAsync(group.Id, default);
            var ends = resumed.Members.Select(m => m.Membership.EndDate).Distinct().ToList();
            Assert.Single(ends);
            Assert.Equal(group.EndDate.AddDays(2), ends[0]);
            Assert.All(resumed.Members, m => Assert.Equal("Active", m.Membership.Status));
        }
    }

    [Fact]
    public async Task A_scheduled_group_freeze_is_undone_for_everyone()
    {
        var world = await SeedAsync();
        using var scope = Actor(world, Day);
        var sp = scope.ServiceProvider;
        var plan = await CreateGroupPlanAsync(sp, seats: 2, maxFreeze: 30);
        var group = await sp.GetRequiredService<AssignGroupMembershipHandler>().HandleAsync(
            new AssignGroupMembershipRequest(world.Members[0], plan.Id, [world.Members[1]], null), default);

        var holder = group.Members.Single(m => m.IsHolder);
        var frozen = await sp.GetRequiredService<CreateFreezeHandler>().HandleAsync(
            holder.MemberId, holder.Membership.Id, new CreateFreezeRequest(Day.AddDays(5), Day.AddDays(9), "Viaje"), default);

        await sp.GetRequiredService<DeleteFreezeHandler>().HandleAsync(
            holder.MemberId, holder.Membership.Id, frozen.NextFreeze!.Id, default);

        var after = await sp.GetRequiredService<GetMembershipGroupHandler>().HandleAsync(group.Id, default);
        Assert.All(after.Members, m =>
        {
            Assert.Equal(group.EndDate, m.Membership.EndDate);
            Assert.Null(m.Membership.NextFreeze);
        });
    }

    [Fact]
    public async Task Cancelling_the_holder_cancels_the_whole_group()
    {
        var world = await SeedAsync();
        using var scope = Actor(world, Day);
        var sp = scope.ServiceProvider;
        var plan = await CreateGroupPlanAsync(sp, seats: 3);
        var group = await sp.GetRequiredService<AssignGroupMembershipHandler>().HandleAsync(
            new AssignGroupMembershipRequest(world.Members[0], plan.Id, [world.Members[1], world.Members[2]], null), default);

        var holder = group.Members.Single(m => m.IsHolder);
        await sp.GetRequiredService<CancelMembershipHandler>().HandleAsync(
            holder.MemberId, holder.Membership.Id, new CancelMembershipRequest("Se mudaron"), default);

        var after = await sp.GetRequiredService<GetMembershipGroupHandler>().HandleAsync(group.Id, default);
        Assert.All(after.Members, m =>
        {
            Assert.Equal("Cancelled", m.Membership.Status);
            Assert.Equal("Se mudaron", m.Membership.CancellationReason);
        });
        Assert.Equal(0, after.SeatsUsed);
        Assert.Equal(3, after.SeatsFree);

        var reader = sp.GetRequiredService<IMembershipReader>();
        foreach (var id in world.Members.Take(3))
        {
            Assert.False((await reader.GetAccessStatusAsync(id)).CanEnter);
        }
    }

    [Fact]
    public async Task Cancelling_a_beneficiary_removes_only_that_person_and_frees_the_seat()
    {
        var world = await SeedAsync();
        using var scope = Actor(world, Day);
        var sp = scope.ServiceProvider;
        var plan = await CreateGroupPlanAsync(sp, seats: 3);
        var group = await sp.GetRequiredService<AssignGroupMembershipHandler>().HandleAsync(
            new AssignGroupMembershipRequest(world.Members[0], plan.Id, [world.Members[1], world.Members[2]], null), default);

        var leaving = group.Members.Single(m => m.MemberId == world.Members[1]);
        await sp.GetRequiredService<CancelMembershipHandler>().HandleAsync(
            leaving.MemberId, leaving.Membership.Id, new CancelMembershipRequest("Se salió del grupo"), default);

        var after = await sp.GetRequiredService<GetMembershipGroupHandler>().HandleAsync(group.Id, default);
        Assert.Equal(2, after.SeatsUsed);
        Assert.Equal(1, after.SeatsFree);
        Assert.Equal("Cancelled", after.Members.Single(m => m.MemberId == world.Members[1]).Membership.Status);
        Assert.Equal("Active", after.Members.Single(m => m.MemberId == world.Members[0]).Membership.Status);
        Assert.Equal("Active", after.Members.Single(m => m.MemberId == world.Members[2]).Membership.Status);

        // El cupo libre se puede llenar con otra persona.
        var refilled = await sp.GetRequiredService<AddGroupBeneficiaryHandler>().HandleAsync(
            group.Id, new AddGroupBeneficiaryRequest(world.Members[3]), default);
        Assert.Equal(3, refilled.SeatsUsed);
    }

    [Fact]
    public async Task Renewing_a_group_chains_a_new_one_for_the_holder_and_the_remaining_beneficiaries()
    {
        var world = await SeedAsync();
        using var scope = Actor(world, Day);
        var sp = scope.ServiceProvider;
        var plan = await CreateGroupPlanAsync(sp, seats: 3, price: 150000m);
        var group = await sp.GetRequiredService<AssignGroupMembershipHandler>().HandleAsync(
            new AssignGroupMembershipRequest(world.Members[0], plan.Id, [world.Members[1], world.Members[2]], null), default);

        // Uno se sale: la renovación sigue con los que quedan.
        var leaving = group.Members.Single(m => m.MemberId == world.Members[2]);
        await sp.GetRequiredService<CancelMembershipHandler>().HandleAsync(
            leaving.MemberId, leaving.Membership.Id, new CancelMembershipRequest("Se salió del grupo"), default);

        var renewed = await sp.GetRequiredService<RenewGroupMembershipHandler>().HandleAsync(
            group.Id, new RenewGroupMembershipRequest(null, null, null), default);

        Assert.NotEqual(group.Id, renewed.Id);
        Assert.Equal(group.EndDate.AddDays(1), renewed.StartDate);
        Assert.Equal(150000m, renewed.TotalToCharge);
        Assert.Equal(2, renewed.Members.Count);
        Assert.Equal(
            new[] { world.Members[0], world.Members[1] }.Order(),
            renewed.Members.Select(m => m.MemberId).Order());
        Assert.All(renewed.Members, m => Assert.Equal(renewed.StartDate, m.Membership.StartDate));
        Assert.Equal(150000m, renewed.Members.Single(m => m.IsHolder).Membership.AgreedPrice);
        Assert.Equal(0m, renewed.Members.Single(m => !m.IsHolder).Membership.AgreedPrice);

        // Un nuevo beneficiario puede entrar en la renovación indicando la lista.
        var again = await sp.GetRequiredService<RenewGroupMembershipHandler>().HandleAsync(
            renewed.Id, new RenewGroupMembershipRequest(null, [world.Members[1], world.Members[3]], null), default);
        Assert.Equal(renewed.EndDate.AddDays(1), again.StartDate);
        Assert.Contains(again.Members, m => m.MemberId == world.Members[3]);
    }

    [Fact]
    public async Task Changing_the_plan_of_a_group_membership_is_not_available()
    {
        var world = await SeedAsync();
        using var scope = Actor(world, Day);
        var sp = scope.ServiceProvider;
        var plan = await CreateGroupPlanAsync(sp, seats: 2);
        var other = await sp.GetRequiredService<CreatePlanHandler>().HandleAsync(
            new CreatePlanRequest($"Solo {Guid.NewGuid():N}"[..12], null, 80000m, 30), default);
        var group = await sp.GetRequiredService<AssignGroupMembershipHandler>().HandleAsync(
            new AssignGroupMembershipRequest(world.Members[0], plan.Id, [world.Members[1]], null), default);

        var holder = group.Members.Single(m => m.IsHolder);
        var ex = await Assert.ThrowsAsync<ConflictException>(() => sp.GetRequiredService<ChangePlanHandler>().HandleAsync(
            holder.MemberId, holder.Membership.Id, new ChangePlanRequest(other.Id), default));
        Assert.Contains("grupo", ex.Message);
    }

    private static async Task<PlanResponse> CreateGroupPlanAsync(
        IServiceProvider sp, int seats, decimal price = 100000m, int maxFreeze = 0)
        => await sp.GetRequiredService<CreatePlanHandler>().HandleAsync(
            new CreatePlanRequest(
                $"Grupo {Guid.NewGuid():N}"[..14], null, price, 30, null, maxFreeze, 1, Seats: seats), default);

    private sealed record World(Guid GymId, Guid StaffId, IReadOnlyList<Guid> Members);

    private IServiceScope Actor(World world, DateOnly day)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(world.StaffId, world.GymId, UserRole.Owner);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(day);
        return scope;
    }

    /// <summary>Un gym con su Owner y cuatro miembros (titular, dos beneficiarios y uno de reserva).</summary>
    private async Task<World> SeedAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, members.members, members.member_invitations, memberships.membership_plans, memberships.membership_groups, memberships.memberships restart identity cascade;");

        var gym = Gym.Create("Group Gym", $"group-{Guid.NewGuid():N}"[..20]);
        var staff = new GSM.Infrastructure.Identity.AppUser
        {
            UserName = "owner@group.local",
            Email = "owner@group.local",
            NormalizedUserName = "OWNER@GROUP.LOCAL",
            NormalizedEmail = "OWNER@GROUP.LOCAL",
            EmailConfirmed = true,
            FullName = "Group Owner",
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        };
        gym.AssignOwner(staff.Id);

        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        db.Add(staff);

        var members = new List<Guid>();
        for (var i = 0; i < 4; i++)
        {
            var member = Member.Create(gym.Id, $"Persona{i}", "Grupo", $"doc-group-{i}", new DateOnly(2026, 1, 1));
            db.Add(member);
            members.Add(member.Id);
        }

        await db.SaveChangesAsync();
        return new World(gym.Id, staff.Id, members);
    }
}
