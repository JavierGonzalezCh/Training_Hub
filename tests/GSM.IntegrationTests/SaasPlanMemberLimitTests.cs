using GSM.Application.Memberships;
using GSM.Application.Memberships.Contracts;
using GSM.Application.SuperAdmin;
using GSM.Application.SuperAdmin.Abstractions;
using GSM.Application.SuperAdmin.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Memberships;
using GSM.Domain.SuperAdmin;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>S2: planes del SaaS con límite de miembros, override por cliente y bloqueo de membresías nuevas.</summary>
public sealed class SaasPlanMemberLimitTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = new(2026, 10, 10);

    private const string AtLimitMessage = "Llegaste al límite de 2 miembros activos de tu plan. Habla con GSM para ampliarlo.";

    [Fact]
    public async Task The_three_historic_plans_are_seeded_with_the_approved_limits()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var plans = await scope.ServiceProvider.GetRequiredService<ISaasPlanRepository>().ListAsync(default);

        Assert.Equal(150, plans.Single(p => p.Name == "Basic").MemberLimit);
        Assert.Equal(500, plans.Single(p => p.Name == "Pro").MemberLimit);
        Assert.Null(plans.Single(p => p.Name == "Enterprise").MemberLimit);
    }

    [Fact]
    public async Task A_subscription_without_chosen_plan_uses_the_limit_of_its_legacy_plan_name()
    {
        var w = await SeedAsync(planEnum: GymPlan.Basic, limitOverride: null);

        using var scope = SuperAdmin();
        var limit = await scope.ServiceProvider.GetRequiredService<ISaasPlanRepository>().GetGymLimitAsync(w.GymId, default);

        Assert.NotNull(limit);
        Assert.Equal("Basic", limit!.PlanName);
        Assert.Equal(150, limit.Effective);
        Assert.Null(limit.Override);
    }

    [Fact]
    public async Task The_override_of_a_client_beats_the_limit_of_its_plan()
    {
        var w = await SeedAsync(GymPlan.Enterprise, limitOverride: 7);

        using var scope = SuperAdmin();
        var limit = await scope.ServiceProvider.GetRequiredService<ISaasPlanRepository>().GetGymLimitAsync(w.GymId, default);

        Assert.Null(limit!.PlanLimit);
        Assert.Equal(7, limit.Effective);
    }

    [Fact]
    public async Task At_the_limit_a_new_member_cannot_get_a_membership()
    {
        var w = await SeedAsync(GymPlan.Basic, limitOverride: 2);
        await AddMembershipAsync(w, w.Members[0], Today.AddDays(-5));
        await AddMembershipAsync(w, w.Members[1], Today.AddDays(-5));

        using var scope = Staff(w);
        var ex = await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider
            .GetRequiredService<AssignMembershipHandler>()
            .HandleAsync(w.Members[2], new AssignMembershipRequest(w.PlanId, null), default));

        Assert.Equal(AtLimitMessage, ex.Message);
    }

    [Fact]
    public async Task Below_the_limit_a_new_member_gets_the_membership()
    {
        var w = await SeedAsync(GymPlan.Basic, limitOverride: 2);
        await AddMembershipAsync(w, w.Members[0], Today.AddDays(-5));

        using var scope = Staff(w);
        var created = await scope.ServiceProvider.GetRequiredService<AssignMembershipHandler>()
            .HandleAsync(w.Members[1], new AssignMembershipRequest(w.PlanId, null), default);

        Assert.Equal(Today, created.StartDate);
    }

    [Fact]
    public async Task Renewing_someone_who_already_counts_is_never_blocked_even_at_the_limit()
    {
        var w = await SeedAsync(GymPlan.Basic, limitOverride: 2);
        await AddMembershipAsync(w, w.Members[0], Today.AddDays(-25));   // al día (por vencer)
        await AddMembershipAsync(w, w.Members[1], Today.AddDays(-32));   // venció hace 2 días: en gracia (3)

        using var scope = Staff(w);
        var handler = scope.ServiceProvider.GetRequiredService<AssignMembershipHandler>();

        var early = await handler.HandleAsync(w.Members[0], new AssignMembershipRequest(w.PlanId, null), default);
        Assert.Equal("early", early.RenewalKind);

        var inGrace = await handler.HandleAsync(w.Members[1], new AssignMembershipRequest(w.PlanId, null), default);
        Assert.Equal("in_grace", inGrace.RenewalKind);
    }

    [Fact]
    public async Task A_frozen_member_counts_and_can_be_renewed_at_the_limit()
    {
        var w = await SeedAsync(GymPlan.Basic, limitOverride: 2);
        await AddMembershipAsync(w, w.Members[0], Today.AddDays(-5), freeze: true);
        await AddMembershipAsync(w, w.Members[1], Today.AddDays(-5));

        using var scope = Staff(w);
        var usage = await scope.ServiceProvider.GetRequiredService<GetMemberUsageHandler>().HandleAsync(default);
        Assert.Equal(2, usage.Counted);

        await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider
            .GetRequiredService<AssignMembershipHandler>()
            .HandleAsync(w.Members[2], new AssignMembershipRequest(w.PlanId, null), default));
    }

    [Fact]
    public async Task Expired_and_cancelled_members_do_not_count_but_cannot_come_back_at_the_limit()
    {
        var w = await SeedAsync(GymPlan.Basic, limitOverride: 2);
        await AddMembershipAsync(w, w.Members[0], Today.AddDays(-5));
        await AddMembershipAsync(w, w.Members[1], Today.AddDays(-5));
        await AddMembershipAsync(w, w.Members[2], Today.AddDays(-60));                  // vencida, fuera de la gracia
        await AddMembershipAsync(w, w.Members[3], Today.AddDays(-5), cancel: true);     // cancelada

        using var scope = Staff(w);
        var usage = await scope.ServiceProvider.GetRequiredService<GetMemberUsageHandler>().HandleAsync(default);
        Assert.Equal(2, usage.Counted);
        Assert.Equal(2, usage.Limit);

        var handler = scope.ServiceProvider.GetRequiredService<AssignMembershipHandler>();
        await Assert.ThrowsAsync<ConflictException>(
            () => handler.HandleAsync(w.Members[2], new AssignMembershipRequest(w.PlanId, null), default));
        await Assert.ThrowsAsync<ConflictException>(
            () => handler.HandleAsync(w.Members[3], new AssignMembershipRequest(w.PlanId, null), default));
    }

    [Fact]
    public async Task A_gym_without_a_limit_is_never_blocked()
    {
        var w = await SeedAsync(GymPlan.Enterprise, limitOverride: null);
        foreach (var member in w.Members.Take(4))
        {
            await AddMembershipAsync(w, member, Today.AddDays(-5));
        }

        using var scope = Staff(w);
        var created = await scope.ServiceProvider.GetRequiredService<AssignMembershipHandler>()
            .HandleAsync(w.Members[4], new AssignMembershipRequest(w.PlanId, null), default);
        Assert.Equal(Today, created.StartDate);

        var usage = await scope.ServiceProvider.GetRequiredService<GetMemberUsageHandler>().HandleAsync(default);
        Assert.Null(usage.Limit);
        Assert.Equal(5, usage.Counted);
    }

    [Fact]
    public async Task A_gym_without_subscription_is_never_blocked()
    {
        var w = await SeedAsync(GymPlan.Basic, limitOverride: 1, withSubscription: false);
        await AddMembershipAsync(w, w.Members[0], Today.AddDays(-5));

        using var scope = Staff(w);
        var created = await scope.ServiceProvider.GetRequiredService<AssignMembershipHandler>()
            .HandleAsync(w.Members[1], new AssignMembershipRequest(w.PlanId, null), default);
        Assert.Equal(Today, created.StartDate);
    }

    [Fact]
    public async Task Raising_the_override_unblocks_and_the_detail_shows_the_usage()
    {
        var w = await SeedAsync(GymPlan.Basic, limitOverride: 2);
        await AddMembershipAsync(w, w.Members[0], Today.AddDays(-5));
        await AddMembershipAsync(w, w.Members[1], Today.AddDays(-5));

        using (var scope = SuperAdmin())
        {
            var detail = await scope.ServiceProvider.GetRequiredService<GetGymDetailHandler>().HandleAsync(w.GymId, default);
            Assert.Equal(2, detail.Subscription.CountedMembers);
            Assert.Equal(2, detail.Subscription.MemberLimit);
            Assert.Equal(2, detail.Subscription.MemberLimitOverride);

            var updated = await scope.ServiceProvider.GetRequiredService<UpdateSubscriptionHandler>()
                .HandleAsync(w.GymId, new UpdateSubscriptionRequest(GymPlan.Basic, 100_000m, MemberLimitOverride: 3), default);
            Assert.Equal(3, updated.MemberLimit);

            var portfolio = await scope.ServiceProvider.GetRequiredService<ListGymPortfolioHandler>()
                .HandleAsync(new PortfolioQuery(), default);
            var row = portfolio.Items.Single(i => i.GymId == w.GymId);
            Assert.Equal(3, row.MemberLimit);
            Assert.Equal(2, row.CountedMembers);
        }

        using var staff = Staff(w);
        var created = await staff.ServiceProvider.GetRequiredService<AssignMembershipHandler>()
            .HandleAsync(w.Members[2], new AssignMembershipRequest(w.PlanId, null), default);
        Assert.Equal(Today, created.StartDate);
    }

    [Fact]
    public async Task Clearing_the_override_returns_to_the_limit_of_the_plan()
    {
        var w = await SeedAsync(GymPlan.Pro, limitOverride: 2);

        using var scope = SuperAdmin();
        var updated = await scope.ServiceProvider.GetRequiredService<UpdateSubscriptionHandler>()
            .HandleAsync(w.GymId, new UpdateSubscriptionRequest(GymPlan.Pro, 100_000m, MemberLimitOverride: null), default);

        Assert.Equal(500, updated.MemberLimit);
        Assert.Null(updated.MemberLimitOverride);
        Assert.Equal("Pro", updated.Plan);
    }

    [Fact]
    public async Task The_superadmin_creates_and_edits_a_plan_and_the_gyms_that_use_it_follow()
    {
        var w = await SeedAsync(GymPlan.Basic, limitOverride: null);
        var name = $"Test-{Guid.NewGuid():N}"[..20];
        Guid planId = Guid.Empty;

        try
        {
            using (var scope = SuperAdmin())
            {
                var created = await scope.ServiceProvider.GetRequiredService<CreateSaasPlanHandler>()
                    .HandleAsync(new CreateSaasPlanRequest(name, 40, 90_000m), default);
                planId = created.Id;
                Assert.Equal(40, created.MemberLimit);

                var updated = await scope.ServiceProvider.GetRequiredService<UpdateSubscriptionHandler>()
                    .HandleAsync(w.GymId, new UpdateSubscriptionRequest(GymPlan.Basic, 90_000m, SaasPlanId: planId), default);
                Assert.Equal(name, updated.Plan);
                Assert.Equal(40, updated.MemberLimit);
            }

            using (var scope = SuperAdmin())
            {
                await scope.ServiceProvider.GetRequiredService<UpdateSaasPlanHandler>()
                    .HandleAsync(planId, new UpdateSaasPlanRequest(name, null, 90_000m, true), default);

                var detail = await scope.ServiceProvider.GetRequiredService<GetGymDetailHandler>().HandleAsync(w.GymId, default);
                Assert.Null(detail.Subscription.MemberLimit);
            }
        }
        finally
        {
            await DeleteSubscriptionAndPlanAsync(w.GymId, planId);
        }
    }

    [Fact]
    public async Task Plan_names_are_unique_and_the_limit_must_be_positive()
    {
        using var scope = SuperAdmin();
        var create = scope.ServiceProvider.GetRequiredService<CreateSaasPlanHandler>();

        await Assert.ThrowsAsync<ConflictException>(
            () => create.HandleAsync(new CreateSaasPlanRequest("basic", 10, 0m), default));
        await Assert.ThrowsAsync<ValidationDomainException>(
            () => create.HandleAsync(new CreateSaasPlanRequest($"Zero-{Guid.NewGuid():N}"[..16], 0, 0m), default));
    }

    // ---- Helpers ----

    private IServiceScope SuperAdmin()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>()
            .ActAs(Guid.CreateVersion7(), gymId: null, UserRole.SuperAdmin);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Today);
        return scope;
    }

    private IServiceScope Staff(World w)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(Guid.CreateVersion7(), w.GymId, UserRole.Staff);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Today);
        return scope;
    }

    private async Task AddMembershipAsync(World w, Guid memberId, DateOnly start, bool freeze = false, bool cancel = false)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var membership = Membership.Create(w.GymId, memberId, w.PlanId, start, 30);
        if (freeze)
        {
            membership.Pause(Today);
        }

        if (cancel)
        {
            membership.Cancel("Prueba", DateTimeOffset.UtcNow);
        }

        db.Add(membership);
        await db.SaveChangesAsync();
    }

    private async Task DeleteSubscriptionAndPlanAsync(Guid gymId, Guid planId)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        await db.Set<GymSubscription>().Where(s => s.GymId == gymId).ExecuteDeleteAsync();
        if (planId != Guid.Empty)
        {
            await db.Set<SaasPlan>().Where(p => p.Id == planId).ExecuteDeleteAsync();
        }
    }

    private async Task<World> SeedAsync(GymPlan planEnum, int? limitOverride, bool withSubscription = true)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var gym = Gym.Create("Limit Gym", $"lim-{Guid.NewGuid():N}"[..16]);
        var plan = MembershipPlan.Create(gym.Id, "Mensual", null, 50000m, 30);
        var members = Enumerable.Range(0, 5)
            .Select(i => Member.Create(gym.Id, $"M{i}", "Prueba", $"doc-{Guid.NewGuid():N}"[..12], Today.AddDays(-100)))
            .ToList();
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id), plan);
        db.AddRange(members);

        if (withSubscription)
        {
            var sub = GymSubscription.Create(gym.Id, planEnum, 100_000m, Today.AddDays(-10), Today.AddDays(20));
            sub.SetMemberLimitOverride(limitOverride);
            db.Add(sub);
        }
        else
        {
            // Sin suscripción no hay dónde poner el override: el límite no existe.
            _ = limitOverride;
        }

        await db.SaveChangesAsync();
        return new World(gym.Id, plan.Id, members.Select(m => m.Id).ToList());
    }

    private sealed record World(Guid GymId, Guid PlanId, List<Guid> Members);
}
