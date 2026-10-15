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

/// <summary>MS4 — congelamientos con reglas por plan, programados y calculados (sin tarea en segundo plano).</summary>
public sealed class MembershipFreezeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Day = new(2026, 3, 10);

    [Fact]
    public async Task Plan_rules_cap_the_total_and_the_minimum_and_a_plan_with_zero_days_cannot_freeze()
    {
        var (gymId, ownerId, memberId) = await SeedAsync();
        using var scope = Actor(gymId, ownerId, UserRole.Staff, Day);
        var sp = scope.ServiceProvider;
        var planId = await CreatePlanAsync(sp, maxFreeze: 10, minFreeze: 3);
        var noFreezePlanId = await CreatePlanAsync(sp, maxFreeze: 0, minFreeze: 1);
        var membership = await sp.GetRequiredService<AssignMembershipHandler>()
            .HandleAsync(memberId, new AssignMembershipRequest(planId, null), default);
        var freeze = sp.GetRequiredService<CreateFreezeHandler>();

        // Menos del mínimo por vez.
        await Assert.ThrowsAsync<ValidationDomainException>(() => freeze.HandleAsync(
            memberId, membership.Id, new CreateFreezeRequest(Day.AddDays(2), Day.AddDays(3), "Viaje"), default));

        // 4 días: cabe. Quedan 6.
        await freeze.HandleAsync(memberId, membership.Id, new CreateFreezeRequest(Day.AddDays(2), Day.AddDays(5), "Viaje"), default);

        // 7 días más pasan del total (4 + 7 > 10).
        await Assert.ThrowsAsync<ValidationDomainException>(() => freeze.HandleAsync(
            memberId, membership.Id, new CreateFreezeRequest(Day.AddDays(10), Day.AddDays(16), "Otro"), default));

        // Un tramo de 6 días llena el límite exacto.
        await freeze.HandleAsync(memberId, membership.Id, new CreateFreezeRequest(Day.AddDays(10), Day.AddDays(15), "Otro"), default);

        var list = await sp.GetRequiredService<ListFreezesHandler>().HandleAsync(memberId, membership.Id, default);
        Assert.Equal(10, list.DaysUsed);
        Assert.Equal(0, list.DaysRemaining);
        Assert.Equal(2, list.Items.Count);

        // Motivo obligatorio.
        await Assert.ThrowsAsync<ValidationDomainException>(() => freeze.HandleAsync(
            memberId, membership.Id, new CreateFreezeRequest(Day.AddDays(20), Day.AddDays(25), " "), default));

        // Un plan con 0 días no se congela.
        var other = await sp.GetRequiredService<AssignMembershipHandler>()
            .HandleAsync(memberId, new AssignMembershipRequest(noFreezePlanId, null), default);
        var ex = await Assert.ThrowsAsync<ValidationDomainException>(() => freeze.HandleAsync(
            memberId, other.Id, new CreateFreezeRequest(Day.AddDays(40), Day.AddDays(45), "Viaje"), default));
        Assert.Contains("no permite congelar", ex.Message);
    }

    [Fact]
    public async Task Only_the_owner_can_override_the_limit_and_it_is_recorded()
    {
        var (gymId, ownerId, memberId) = await SeedAsync();
        Guid membershipId;
        using (var staff = Actor(gymId, ownerId, UserRole.Staff, Day))
        {
            var sp = staff.ServiceProvider;
            var planId = await CreatePlanAsync(sp, maxFreeze: 0, minFreeze: 1);
            membershipId = (await sp.GetRequiredService<AssignMembershipHandler>()
                .HandleAsync(memberId, new AssignMembershipRequest(planId, null), default)).Id;

            await Assert.ThrowsAsync<ForbiddenDomainException>(() => sp.GetRequiredService<CreateFreezeHandler>().HandleAsync(
                memberId, membershipId, new CreateFreezeRequest(Day.AddDays(1), Day.AddDays(5), "Lesión", OverrideLimit: true), default));
        }

        using (var owner = Actor(gymId, ownerId, UserRole.Owner, Day))
        {
            var sp = owner.ServiceProvider;
            var response = await sp.GetRequiredService<CreateFreezeHandler>().HandleAsync(
                memberId, membershipId, new CreateFreezeRequest(Day.AddDays(1), Day.AddDays(5), "Lesión", OverrideLimit: true), default);

            Assert.True(response.NextFreeze!.OverrideLimit);
            Assert.Equal(5, response.NextFreeze.DaysCredited);
        }
    }

    [Fact]
    public async Task A_scheduled_freeze_starts_and_ends_by_itself_and_moves_the_end_and_the_chained_renewal()
    {
        var (gymId, ownerId, memberId) = await SeedAsync();
        MembershipResponse current;
        MembershipResponse renewal;

        using (var scope = Actor(gymId, ownerId, UserRole.Staff, Day))
        {
            var sp = scope.ServiceProvider;
            var planId = await CreatePlanAsync(sp, maxFreeze: 30, minFreeze: 1);
            var assign = sp.GetRequiredService<AssignMembershipHandler>();
            current = await assign.HandleAsync(memberId, new AssignMembershipRequest(planId, null), default);
            renewal = await assign.HandleAsync(memberId, new AssignMembershipRequest(planId, null), default);

            var frozen = await sp.GetRequiredService<CreateFreezeHandler>().HandleAsync(
                memberId, current.Id, new CreateFreezeRequest(Day.AddDays(5), Day.AddDays(9), "Viaje"), default);

            // Programado: el fin se corrió ya 5 días, hoy sigue activa.
            Assert.Equal(current.EndDate.AddDays(5), frozen.EndDate);
            Assert.Equal("Active", frozen.Status);
            Assert.Equal(Day.AddDays(5), frozen.NextFreeze!.StartsOn);
            Assert.Equal("scheduled", frozen.NextFreeze.State);

            var history = await sp.GetRequiredService<ListMemberMembershipsHandler>().HandleAsync(memberId, default);
            var shifted = history.Single(m => m.Id == renewal.Id);
            Assert.Equal(renewal.StartDate.AddDays(5), shifted.StartDate);
            Assert.Equal(frozen.EndDate.AddDays(1), shifted.StartDate);
        }

        // Antes de empezar: cubierto. Durante: congelado, sin entrada. Después: cubierto otra vez. Sin intervención.
        await AssertAccessAsync(gymId, ownerId, memberId, Day.AddDays(4), CommercialStatus.Current, canEnter: true);
        await AssertAccessAsync(gymId, ownerId, memberId, Day.AddDays(5), CommercialStatus.Frozen, canEnter: false);
        await AssertAccessAsync(gymId, ownerId, memberId, Day.AddDays(9), CommercialStatus.Frozen, canEnter: false);
        await AssertAccessAsync(gymId, ownerId, memberId, Day.AddDays(10), CommercialStatus.Current, canEnter: true);

        // El lector de "cubierto hoy" también lo respeta (sin tarea, solo el cálculo).
        using (var scope = Actor(gymId, ownerId, UserRole.Staff, Day.AddDays(6)))
        {
            Assert.False(await scope.ServiceProvider.GetRequiredService<IMembershipReader>().IsActiveAsync(memberId));
            var list = await scope.ServiceProvider.GetRequiredService<ListMemberMembershipsHandler>().HandleAsync(memberId, default);
            Assert.Equal("Paused", list.Single(m => m.Id == current.Id).Status);
        }

        using (var scope = Actor(gymId, ownerId, UserRole.Staff, Day.AddDays(10)))
        {
            Assert.True(await scope.ServiceProvider.GetRequiredService<IMembershipReader>().IsActiveAsync(memberId));
        }
    }

    [Fact]
    public async Task Resuming_a_scheduled_freeze_early_gives_back_the_unused_days_and_pulls_the_chain_back()
    {
        var (gymId, ownerId, memberId) = await SeedAsync();
        MembershipResponse current;
        MembershipResponse renewal;

        using (var scope = Actor(gymId, ownerId, UserRole.Staff, Day))
        {
            var sp = scope.ServiceProvider;
            var planId = await CreatePlanAsync(sp, maxFreeze: 30, minFreeze: 1);
            var assign = sp.GetRequiredService<AssignMembershipHandler>();
            current = await assign.HandleAsync(memberId, new AssignMembershipRequest(planId, null), default);
            renewal = await assign.HandleAsync(memberId, new AssignMembershipRequest(planId, null), default);
            await sp.GetRequiredService<CreateFreezeHandler>().HandleAsync(
                memberId, current.Id, new CreateFreezeRequest(Day.AddDays(2), Day.AddDays(11), "Viaje"), default); // 10 días
        }

        using (var scope = Actor(gymId, ownerId, UserRole.Staff, Day.AddDays(5)))
        {
            var sp = scope.ServiceProvider;
            var resumed = await sp.GetRequiredService<ResumeMembershipHandler>().HandleAsync(memberId, current.Id, default);

            // Congelada del 2 al 4 (3 días): el fin queda +3, no +10.
            Assert.Equal(current.EndDate.AddDays(3), resumed.EndDate);
            Assert.Null(resumed.ActiveFreeze);

            var history = await sp.GetRequiredService<ListMemberMembershipsHandler>().HandleAsync(memberId, default);
            var shifted = history.Single(m => m.Id == renewal.Id);
            Assert.Equal(resumed.EndDate.AddDays(1), shifted.StartDate);
            Assert.Equal(renewal.EndDate.AddDays(3), shifted.EndDate);

            var list = await sp.GetRequiredService<ListFreezesHandler>().HandleAsync(memberId, current.Id, default);
            var only = Assert.Single(list.Items);
            Assert.Equal(Day.AddDays(4), only.EndsOn);
            Assert.Equal(3, only.DaysCredited);
            Assert.Equal("ended", only.State);

            // Ya no está congelada.
            await Assert.ThrowsAsync<ConflictException>(() => sp.GetRequiredService<ResumeMembershipHandler>()
                .HandleAsync(memberId, current.Id, default));
        }
    }

    [Fact]
    public async Task A_scheduled_freeze_that_has_not_started_can_be_deleted_and_one_that_started_cannot()
    {
        var (gymId, ownerId, memberId) = await SeedAsync();
        Guid membershipId;
        Guid freezeId;
        DateOnly originalEnd;

        using (var scope = Actor(gymId, ownerId, UserRole.Staff, Day))
        {
            var sp = scope.ServiceProvider;
            var planId = await CreatePlanAsync(sp, maxFreeze: 30, minFreeze: 1);
            var membership = await sp.GetRequiredService<AssignMembershipHandler>()
                .HandleAsync(memberId, new AssignMembershipRequest(planId, null), default);
            membershipId = membership.Id;
            originalEnd = membership.EndDate;

            var frozen = await sp.GetRequiredService<CreateFreezeHandler>().HandleAsync(
                memberId, membershipId, new CreateFreezeRequest(Day.AddDays(3), Day.AddDays(7), "Viaje"), default);
            freezeId = frozen.NextFreeze!.Id;
            Assert.Equal(originalEnd.AddDays(5), frozen.EndDate);

            var undone = await sp.GetRequiredService<DeleteFreezeHandler>().HandleAsync(memberId, membershipId, freezeId, default);
            Assert.Equal(originalEnd, undone.EndDate);
            Assert.Null(undone.NextFreeze);
            var list = await sp.GetRequiredService<ListFreezesHandler>().HandleAsync(memberId, membershipId, default);
            Assert.Empty(list.Items);

            await Assert.ThrowsAsync<NotFoundException>(() => sp.GetRequiredService<DeleteFreezeHandler>()
                .HandleAsync(memberId, membershipId, freezeId, default));

            var again = await sp.GetRequiredService<CreateFreezeHandler>().HandleAsync(
                memberId, membershipId, new CreateFreezeRequest(Day.AddDays(3), Day.AddDays(7), "Viaje"), default);
            freezeId = again.NextFreeze!.Id;
        }

        using (var scope = Actor(gymId, ownerId, UserRole.Staff, Day.AddDays(4)))
        {
            await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider.GetRequiredService<DeleteFreezeHandler>()
                .HandleAsync(memberId, membershipId, freezeId, default));
        }
    }

    [Fact]
    public async Task An_open_freeze_credits_at_most_the_plan_limit_unless_the_owner_overrode_it()
    {
        var (gymId, ownerId, memberId) = await SeedAsync();
        MembershipResponse membership;

        using (var scope = Actor(gymId, ownerId, UserRole.Staff, Day))
        {
            var sp = scope.ServiceProvider;
            var planId = await CreatePlanAsync(sp, maxFreeze: 5, minFreeze: 2);
            membership = await sp.GetRequiredService<AssignMembershipHandler>()
                .HandleAsync(memberId, new AssignMembershipRequest(planId, null), default);
            var open = await sp.GetRequiredService<CreateFreezeHandler>().HandleAsync(
                memberId, membership.Id, new CreateFreezeRequest(null, null, "Lesión"), default);

            Assert.Equal("Paused", open.Status);
            Assert.Null(open.ActiveFreeze!.EndsOn);
            Assert.Equal(membership.EndDate, open.EndDate); // el fin se corre al reanudar

            // Abierto solo desde hoy.
            await Assert.ThrowsAsync<ConflictException>(() => sp.GetRequiredService<CreateFreezeHandler>().HandleAsync(
                memberId, membership.Id, new CreateFreezeRequest(null, null, "Otra vez"), default));
        }

        using (var scope = Actor(gymId, ownerId, UserRole.Staff, Day.AddDays(8)))
        {
            var resumed = await scope.ServiceProvider.GetRequiredService<ResumeMembershipHandler>()
                .HandleAsync(memberId, membership.Id, default);

            // Estuvo 8 días congelada pero el plan solo permite 5.
            Assert.Equal(membership.EndDate.AddDays(5), resumed.EndDate);
        }
    }

    [Fact]
    public async Task Paused_memberships_from_before_become_an_open_freeze_with_the_migration_script()
    {
        var (gymId, ownerId, memberId) = await SeedAsync();
        Guid membershipId;

        using (var scope = Actor(gymId, ownerId, UserRole.Staff, Day))
        {
            var sp = scope.ServiceProvider;
            var planId = await CreatePlanAsync(sp, maxFreeze: 0, minFreeze: 1);
            membershipId = (await sp.GetRequiredService<AssignMembershipHandler>()
                .HandleAsync(memberId, new AssignMembershipRequest(planId, null), default)).Id;
        }

        using (var scope = factory.CreateScopeWithMigration())
        {
            scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "update memberships.memberships set status = 'paused' where id = {0}", membershipId);
            await db.Database.ExecuteSqlRawAsync(MigrationScripts.Read("MS4"));
            await db.Database.ExecuteSqlRawAsync(MigrationScripts.Read("MS4")); // idempotente
            // Sin paused_on en la fila (la columna ya no existe) el inicio sale de updated_at; lo fijamos en el día del test.
            await db.Database.ExecuteSqlRawAsync(
                "update memberships.membership_freezes set starts_on = '2026-03-12' where membership_id = {0}", membershipId);
        }

        using (var scope = Actor(gymId, ownerId, UserRole.Staff, Day.AddDays(4)))
        {
            var sp = scope.ServiceProvider;
            var list = await sp.GetRequiredService<ListFreezesHandler>().HandleAsync(memberId, membershipId, default);
            var migrated = Assert.Single(list.Items);
            Assert.Equal("Migrado", migrated.Reason);
            Assert.Null(migrated.EndsOn);
            Assert.Equal("active", migrated.State);

            var access = await sp.GetRequiredService<IMembershipReader>().GetAccessStatusAsync(memberId);
            Assert.Equal(CommercialStatus.Frozen, access.Commercial);

            // Al reanudar se acreditan todos los días congelados aunque el plan migrado tenga 0.
            var resumed = await sp.GetRequiredService<ResumeMembershipHandler>().HandleAsync(memberId, membershipId, default);
            Assert.Equal("Active", resumed.Status);
        }
    }

    private async Task AssertAccessAsync(
        Guid gymId, Guid actorId, Guid memberId, DateOnly day, CommercialStatus expected, bool canEnter)
    {
        using var scope = Actor(gymId, actorId, UserRole.Staff, day);
        var access = await scope.ServiceProvider.GetRequiredService<IMembershipReader>().GetAccessStatusAsync(memberId);
        Assert.Equal(expected, access.Commercial);
        Assert.Equal(canEnter, access.CanEnter);
        if (expected == CommercialStatus.Frozen)
        {
            Assert.Equal(AccessBlockReason.Frozen, access.BlockReason);
            Assert.Contains("congelada", access.BlockMessage);
        }
    }

    private static async Task<Guid> CreatePlanAsync(IServiceProvider sp, int maxFreeze, int minFreeze)
        => (await sp.GetRequiredService<CreatePlanHandler>().HandleAsync(
            new CreatePlanRequest($"Mensual {Guid.NewGuid():N}"[..20], null, 80000m, 30, null, maxFreeze, minFreeze), default)).Id;

    private IServiceScope Actor(Guid gymId, Guid userId, UserRole role, DateOnly day)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(userId, gymId, role);
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

        var gym = Gym.Create("Freeze Gym", $"freeze-{Guid.NewGuid():N}"[..20]);
        var staff = new GSM.Infrastructure.Identity.AppUser
        {
            UserName = "staff@freeze.local",
            Email = "staff@freeze.local",
            NormalizedUserName = "STAFF@FREEZE.LOCAL",
            NormalizedEmail = "STAFF@FREEZE.LOCAL",
            EmailConfirmed = true,
            FullName = "Freeze Staff",
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        };
        gym.AssignOwner(staff.Id);
        var member = Member.Create(gym.Id, "Freeze", "Member", "doc-freeze", new DateOnly(2026, 1, 1));

        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        db.Add(staff);
        db.Add(member);
        await db.SaveChangesAsync();

        return (gym.Id, staff.Id, member.Id);
    }
}
