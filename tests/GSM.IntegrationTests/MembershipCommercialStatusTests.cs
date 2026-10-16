using GSM.Application.Dashboard;
using GSM.Application.Members;
using GSM.Application.Members.Contracts;
using GSM.Application.Memberships;
using GSM.Application.Memberships.Contracts;
using GSM.Application.Memberships.Public;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Memberships;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>Estado comercial, gracia en Memberships, regla de renovación y reactivar planes (MS1).</summary>
public sealed class MembershipCommercialStatusTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = new(2026, 10, 10);

    private static MembershipSpan Active(DateOnly start, DateOnly end) => new(MembershipStatus.Active, start, end);

    // ---- Calculador (puro): cada caso de §10 y sus bordes ----

    [Theory]
    [InlineData(30, CommercialStatus.Current, AccessKind.Covered)]
    [InlineData(8, CommercialStatus.Current, AccessKind.Covered)]
    [InlineData(7, CommercialStatus.Expiring, AccessKind.Expiring)]   // borde: justo la ventana
    [InlineData(1, CommercialStatus.Expiring, AccessKind.Expiring)]
    [InlineData(0, CommercialStatus.Expiring, AccessKind.Expiring)]   // vence hoy: todavía cubre
    public void A_membership_that_covers_today_is_current_or_expiring(int daysLeft, CommercialStatus expected, AccessKind kind)
    {
        var access = CommercialStatusCalculator.Compute(
            [Active(Today.AddDays(-20), Today.AddDays(daysLeft))], Today, graceDays: 3, expiringDays: 7);

        Assert.Equal(expected, access.Commercial);
        Assert.Equal(kind, access.Kind);
        Assert.True(access.CanEnter);
        Assert.Equal(daysLeft, access.DaysLeft);
    }

    [Fact]
    public void Expiring_notice_says_when_it_ends_and_today_is_special()
    {
        var in3 = CommercialStatusCalculator.Compute([Active(Today.AddDays(-20), Today.AddDays(3))], Today, 3, 7);
        Assert.Equal("Tu plan vence en 3 días (el 13 oct). Renueva en recepción para no perder tu fecha.", in3.Notice);

        var today = CommercialStatusCalculator.Compute([Active(Today.AddDays(-20), Today)], Today, 3, 7);
        Assert.Equal("Tu plan vence hoy.", today.Notice);

        var far = CommercialStatusCalculator.Compute([Active(Today.AddDays(-20), Today.AddDays(20))], Today, 3, 7);
        Assert.Null(far.Notice);
    }

    [Theory]
    [InlineData(1, 2)]   // venció ayer, gracia 3: quedan 2
    [InlineData(3, 0)]   // borde: último día de gracia, todavía entra
    public void Within_the_grace_the_member_is_in_grace_and_enters_with_a_notice(int endedDaysAgo, int graceLeft)
    {
        var end = Today.AddDays(-endedDaysAgo);
        var access = CommercialStatusCalculator.Compute([Active(end.AddDays(-29), end)], Today, 3, 7);

        Assert.Equal(CommercialStatus.Grace, access.Commercial);
        Assert.Equal(AccessKind.Grace, access.Kind);
        Assert.True(access.CanEnter);
        Assert.Equal(graceLeft, access.GraceDaysLeft);
        Assert.Equal(end.AddDays(3), access.GraceEndsOn);
        Assert.Contains($"Tu plan venció el {end.Day} oct.", access.Notice);
        Assert.Contains($"hasta el {end.AddDays(3).Day} oct", access.Notice);
    }

    [Fact]
    public void Past_the_grace_it_is_expired_and_blocked()
    {
        var end = Today.AddDays(-4);
        var access = CommercialStatusCalculator.Compute([Active(end.AddDays(-29), end)], Today, 3, 7);

        Assert.Equal(CommercialStatus.Expired, access.Commercial);
        Assert.Equal(AccessKind.Blocked, access.Kind);
        Assert.Equal(AccessBlockReason.Expired, access.BlockReason);
        Assert.Equal("Membresía vencida el 6 oct, fuera de la gracia.", access.BlockMessage);
    }

    [Fact]
    public void No_membership_cancelled_only_frozen_and_future_only_cases()
    {
        Assert.Equal(CommercialStatus.NoPlan, CommercialStatusCalculator.Compute([], Today, 3, 7).Commercial);

        var cancelled = new MembershipSpan(MembershipStatus.Cancelled, Today.AddDays(-5), Today.AddDays(25));
        Assert.Equal(CommercialStatus.NoPlan, CommercialStatusCalculator.Compute([cancelled], Today, 3, 7).Commercial);

        var frozen = new MembershipSpan(MembershipStatus.Paused, Today.AddDays(-20), Today.AddDays(10));
        var frozenAccess = CommercialStatusCalculator.Compute([frozen], Today, 3, 7);
        Assert.Equal(CommercialStatus.Frozen, frozenAccess.Commercial);
        Assert.Equal(AccessBlockReason.Frozen, frozenAccess.BlockReason);

        var future = Active(Today.AddDays(3), Today.AddDays(33));
        var scheduled = CommercialStatusCalculator.Compute([future], Today, 3, 7);
        Assert.Equal(CommercialStatus.Scheduled, scheduled.Commercial);
        Assert.Equal(Today.AddDays(3), scheduled.StartsOn);
    }

    [Fact]
    public void A_single_membership_that_starts_tomorrow_is_scheduled_with_its_start_date()
    {
        var access = CommercialStatusCalculator.Compute([Active(Today.AddDays(1), Today.AddDays(30))], Today, 3, 7);

        Assert.Equal(CommercialStatus.Scheduled, access.Commercial);
        Assert.Equal(AccessKind.Blocked, access.Kind);
        Assert.Equal(AccessBlockReason.Scheduled, access.BlockReason);
        Assert.Equal(Today.AddDays(1), access.StartsOn);
        Assert.False(access.CanEnter);
        Assert.Equal("Programada: empieza el 11 oct.", access.BlockMessage);
        Assert.False(access.Commercial.CountsTowardMemberLimit());
    }

    [Fact]
    public void An_expired_membership_in_grace_plus_a_future_one_is_grace_and_past_the_grace_is_scheduled()
    {
        var future = Active(Today.AddDays(5), Today.AddDays(34));

        var inGrace = CommercialStatusCalculator.Compute([Active(Today.AddDays(-31), Today.AddDays(-1)), future], Today, 3, 7);
        Assert.Equal(CommercialStatus.Grace, inGrace.Commercial);
        Assert.Null(inGrace.StartsOn);

        var pastGrace = CommercialStatusCalculator.Compute([Active(Today.AddDays(-40), Today.AddDays(-10)), future], Today, 3, 7);
        Assert.Equal(CommercialStatus.Scheduled, pastGrace.Commercial);
        Assert.Equal(Today.AddDays(5), pastGrace.StartsOn);
    }

    [Fact]
    public async Task Scheduled_is_not_counted_as_no_plan_in_the_list_or_the_dashboard_and_can_be_filtered()
    {
        var w = await SeedAsync();
        await AddMembershipAsync(w, w.Members[0], Today.AddDays(1), 30);    // empieza mañana
        // Members[1..4]: sin plan.

        using var s = Scope(w, UserRole.Staff);
        var list = s.ServiceProvider.GetRequiredService<ListMembersHandler>();

        var scheduled = await list.HandleAsync(new ListMembersQuery { CommercialStatus = "scheduled", PageSize = 50 }, default);
        var item = Assert.Single(scheduled.Items);
        Assert.Equal(w.Members[0], item.Id);
        Assert.Equal("scheduled", item.CommercialStatus);

        var noPlan = await list.HandleAsync(new ListMembersQuery { CommercialStatus = "no_plan", PageSize = 50 }, default);
        Assert.Equal(4, noPlan.Total);
        Assert.DoesNotContain(noPlan.Items, i => i.Id == w.Members[0]);

        var dashboard = await s.ServiceProvider.GetRequiredService<GetDashboardHandler>().HandleAsync(default);
        Assert.Equal(1, dashboard.Indicators!.CommercialStatus.Scheduled);
        Assert.Equal(4, dashboard.Indicators.CommercialStatus.NoPlan);

        var reader = s.ServiceProvider.GetRequiredService<IMembershipReader>();
        var access = await reader.GetAccessStatusAsync(w.Members[0]);
        Assert.Equal(CommercialStatus.Scheduled, access.Commercial);
        Assert.Equal(Today.AddDays(1), access.StartsOn);
    }

    [Fact]
    public void An_early_renewal_chained_behind_the_current_one_is_not_expiring()
    {
        var current = Active(Today.AddDays(-25), Today.AddDays(4));
        var next = Active(Today.AddDays(5), Today.AddDays(34));

        var access = CommercialStatusCalculator.Compute([current, next], Today, 3, 7);

        Assert.Equal(CommercialStatus.Current, access.Commercial);
        Assert.Equal(34, access.DaysLeft);
    }

    // ---- Regla de renovación (puro) ----

    [Fact]
    public void Renewal_planner_covers_the_four_cases_of_4_1()
    {
        var none = RenewalPlanner.Plan([], Today, 3);
        Assert.Equal((RenewalKind.New, Today), (none.Kind, none.StartDate));

        var cancelledOnly = RenewalPlanner.Plan(
            [new MembershipSpan(MembershipStatus.Cancelled, Today.AddDays(-5), Today.AddDays(25))], Today, 3);
        Assert.Equal(RenewalKind.New, cancelledOnly.Kind);

        var early = RenewalPlanner.Plan([Active(Today.AddDays(-20), Today.AddDays(9))], Today, 3);
        Assert.Equal((RenewalKind.Early, Today.AddDays(10)), (early.Kind, early.StartDate));

        // Vencía el 7 y hoy es 10 (3 días de gracia): conserva el ciclo, empieza el 8.
        var inGrace = RenewalPlanner.Plan([Active(Today.AddDays(-32), Today.AddDays(-3))], Today, 3);
        Assert.Equal((RenewalKind.InGrace, Today.AddDays(-2)), (inGrace.Kind, inGrace.StartDate));

        // Un día más y ya pasó la gracia: empieza hoy.
        var after = RenewalPlanner.Plan([Active(Today.AddDays(-33), Today.AddDays(-4))], Today, 3);
        Assert.Equal((RenewalKind.AfterGrace, Today), (after.Kind, after.StartDate));
    }

    [Fact]
    public void Changing_the_start_needs_a_reason_unless_it_is_the_first_membership_and_cannot_overlap()
    {
        var early = RenewalPlanner.Plan([Active(Today.AddDays(-20), Today.AddDays(9))], Today, 3);

        Assert.Throws<ValidationDomainException>(() => RenewalPlanner.ApplyOverride(early, Today.AddDays(15), null));
        Assert.Throws<ValidationDomainException>(() => RenewalPlanner.ApplyOverride(early, Today.AddDays(5), "Motivo válido"));
        var (start, reason) = RenewalPlanner.ApplyOverride(early, Today.AddDays(15), "Viaja una semana");
        Assert.Equal((Today.AddDays(15), "Viaja una semana"), (start, reason));

        var first = RenewalPlanner.Plan([], Today, 3);
        Assert.Equal(Today.AddDays(2), RenewalPlanner.ApplyOverride(first, Today.AddDays(2), null).StartDate);
    }

    // ---- Con base de datos ----

    [Fact]
    public async Task Without_a_row_the_settings_are_3_and_7_and_the_owner_can_change_them()
    {
        var w = await SeedAsync();

        using (var s = Scope(w, UserRole.Staff))
        {
            var current = await s.ServiceProvider.GetRequiredService<GetMembershipSettingsHandler>().HandleAsync(default);
            Assert.Equal((3, 7), (current.GraceDays, current.ExpiringDays));
            var view = await s.ServiceProvider.GetRequiredService<IMembershipReader>().GetSettingsAsync(w.GymId);
            Assert.Equal((3, 7), (view.GraceDays, view.ExpiringDays));
            Assert.Equal(0, await s.ServiceProvider.GetRequiredService<GsmDbContext>()
                .Set<MembershipSettings>().CountAsync(x => x.GymId == w.GymId));
        }

        using (var s = Scope(w, UserRole.Owner))
        {
            await s.ServiceProvider.GetRequiredService<UpdateMembershipSettingsHandler>()
                .HandleAsync(new UpdateMembershipSettingsRequest(5, 10, 0m), default);
            await Assert.ThrowsAsync<ValidationDomainException>(() => s.ServiceProvider
                .GetRequiredService<UpdateMembershipSettingsHandler>()
                .HandleAsync(new UpdateMembershipSettingsRequest(31, 10, 0m), default));
        }

        using (var s = Scope(w, UserRole.Staff))
        {
            var view = await s.ServiceProvider.GetRequiredService<IMembershipReader>().GetSettingsAsync(w.GymId);
            Assert.Equal((5, 10), (view.GraceDays, view.ExpiringDays));
        }
    }

    [Fact]
    public async Task Reader_resolves_the_access_status_with_the_gym_grace_and_the_business_day()
    {
        var w = await SeedAsync();
        var plan = w.PlanId;
        await AddMembershipAsync(w, w.Members[0], Today.AddDays(-29), 30);   // vence hoy: 0 días
        await AddMembershipAsync(w, w.Members[1], Today.AddDays(-32), 30);   // venció el 7 (hoy 10): gracia
        await AddMembershipAsync(w, w.Members[2], Today.AddDays(-40), 30);   // venció el 1: vencido

        using var s = Scope(w, UserRole.Staff);
        var reader = s.ServiceProvider.GetRequiredService<IMembershipReader>();

        Assert.Equal(CommercialStatus.Expiring, (await reader.GetAccessStatusAsync(w.Members[0])).Commercial);
        var grace = await reader.GetAccessStatusAsync(w.Members[1]);
        Assert.Equal(CommercialStatus.Grace, grace.Commercial);
        Assert.Equal(0, grace.GraceDaysLeft);
        Assert.Equal(CommercialStatus.Expired, (await reader.GetAccessStatusAsync(w.Members[2])).Commercial);
        Assert.Equal(CommercialStatus.NoPlan, (await reader.GetAccessStatusAsync(w.Members[3])).Commercial);
        _ = plan;
    }

    [Fact]
    public async Task Assigning_stores_the_renewal_kind_and_the_preview_matches_what_is_created()
    {
        var w = await SeedAsync();
        var memberId = w.Members[0];

        async Task<(RenewalPreviewResponse Preview, MembershipResponse Created)> AssignAsync(DateOnly? start = null, string? reason = null)
        {
            using var s = Scope(w, UserRole.Staff);
            var preview = await s.ServiceProvider.GetRequiredService<PreviewMembershipHandler>()
                .HandleAsync(memberId, w.PlanId, start, null, default);
            var created = await s.ServiceProvider.GetRequiredService<AssignMembershipHandler>()
                .HandleAsync(memberId, new AssignMembershipRequest(w.PlanId, start, reason), default);
            Assert.Equal(preview.StartDate, created.StartDate);
            Assert.Equal(preview.EndDate, created.EndDate);
            Assert.Equal(preview.Kind, created.RenewalKind);
            return (preview, created);
        }

        // new: hoy.
        var first = (await AssignAsync()).Created;
        Assert.Equal("new", first.RenewalKind);
        Assert.Equal(Today, first.StartDate);
        Assert.Equal(Today.AddDays(29), first.EndDate);

        // early: la siguiente va detrás.
        var second = (await AssignAsync()).Created;
        Assert.Equal("early", second.RenewalKind);
        Assert.Equal(Today.AddDays(30), second.StartDate);
    }

    [Fact]
    public async Task Renewal_inside_the_grace_keeps_the_cycle_and_after_the_grace_starts_today()
    {
        var w = await SeedAsync();
        await AddMembershipAsync(w, w.Members[0], Today.AddDays(-32), 30);   // venció el 7 oct
        await AddMembershipAsync(w, w.Members[1], Today.AddDays(-40), 30);   // venció el 1 oct

        using var s = Scope(w, UserRole.Staff);
        var handler = s.ServiceProvider.GetRequiredService<AssignMembershipHandler>();

        var inGrace = await handler.HandleAsync(w.Members[0], new AssignMembershipRequest(w.PlanId, null), default);
        Assert.Equal("in_grace", inGrace.RenewalKind);
        Assert.Equal(new DateOnly(2026, 10, 8), inGrace.StartDate);
        Assert.Equal(new DateOnly(2026, 11, 6), inGrace.EndDate);

        var after = await handler.HandleAsync(w.Members[1], new AssignMembershipRequest(w.PlanId, null), default);
        Assert.Equal("after_grace", after.RenewalKind);
        Assert.Equal(Today, after.StartDate);
    }

    [Fact]
    public async Task Changing_the_start_of_a_renewal_needs_a_reason_and_it_is_recorded()
    {
        var w = await SeedAsync();
        await AddMembershipAsync(w, w.Members[0], Today.AddDays(-20), 30);   // vence en 9 días

        using var s = Scope(w, UserRole.Staff);
        var handler = s.ServiceProvider.GetRequiredService<AssignMembershipHandler>();
        var chosen = Today.AddDays(15);

        await Assert.ThrowsAsync<ValidationDomainException>(
            () => handler.HandleAsync(w.Members[0], new AssignMembershipRequest(w.PlanId, chosen), default));

        var created = await handler.HandleAsync(
            w.Members[0], new AssignMembershipRequest(w.PlanId, chosen, "Viaja"), default);
        Assert.Equal(chosen, created.StartDate);
        Assert.Equal("Viaja", created.StartOverrideReason);
        Assert.Equal("early", created.RenewalKind);
    }

    [Fact]
    public async Task Member_list_shows_and_filters_by_commercial_status_and_dashboard_counts_them()
    {
        var w = await SeedAsync();
        await AddMembershipAsync(w, w.Members[0], Today.AddDays(-5), 30);    // al día (25 días)
        await AddMembershipAsync(w, w.Members[1], Today.AddDays(-25), 30);   // por vencer (4)
        await AddMembershipAsync(w, w.Members[2], Today.AddDays(-32), 30);   // en gracia
        await AddMembershipAsync(w, w.Members[3], Today.AddDays(-50), 30);   // vencido
        // Members[4]: sin plan.

        using var s = Scope(w, UserRole.Staff);
        var list = s.ServiceProvider.GetRequiredService<ListMembersHandler>();

        var all = await list.HandleAsync(new ListMembersQuery { PageSize = 50 }, default);
        Assert.Equal(5, all.Total);
        Assert.Equal(
            ["current", "expiring", "grace", "expired", "no_plan"],
            w.Members.Select(id => all.Items.Single(i => i.Id == id).CommercialStatus));

        foreach (var (status, index) in new[] { ("expiring", 1), ("grace", 2), ("expired", 3), ("no_plan", 4), ("current", 0) })
        {
            var page = await list.HandleAsync(new ListMembersQuery { CommercialStatus = status, PageSize = 50 }, default);
            Assert.Equal(w.Members[index], Assert.Single(page.Items).Id);
        }

        var dashboard = await s.ServiceProvider.GetRequiredService<GetDashboardHandler>().HandleAsync(default);
        Assert.Equal(new CommercialStatusCounts(1, 1, 1, 1, 0, 1, 0), dashboard.Indicators!.CommercialStatus);
        Assert.Equal(7, dashboard.Indicators.ExpiringDays);
    }

    [Fact]
    public async Task A_deactivated_member_is_not_counted_in_the_dashboard_by_status()
    {
        var w = await SeedAsync();
        await AddMembershipAsync(w, w.Members[0], Today.AddDays(-5), 30);
        using (var s = Scope(w, UserRole.Staff))
        {
            await s.ServiceProvider.GetRequiredService<DeactivateMemberHandler>().HandleAsync(w.Members[0], default);
        }

        using var scope = Scope(w, UserRole.Staff);
        var dashboard = await scope.ServiceProvider.GetRequiredService<GetDashboardHandler>().HandleAsync(default);
        Assert.Equal(0, dashboard.Indicators!.CommercialStatus.Current);
    }

    [Fact]
    public async Task A_deactivated_plan_can_be_reactivated_and_an_active_one_is_a_conflict()
    {
        var w = await SeedAsync();
        using var s = Scope(w, UserRole.Owner);

        await s.ServiceProvider.GetRequiredService<DeletePlanHandler>().HandleAsync(w.PlanId, default);
        var reactivated = await s.ServiceProvider.GetRequiredService<ReactivatePlanHandler>().HandleAsync(w.PlanId, default);
        Assert.True(reactivated.IsActive);

        await Assert.ThrowsAsync<ConflictException>(
            () => s.ServiceProvider.GetRequiredService<ReactivatePlanHandler>().HandleAsync(w.PlanId, default));
    }

    // ---- Helpers ----

    private IServiceScope Scope(World w, UserRole role)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(Guid.CreateVersion7(), w.GymId, role);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Today);
        return scope;
    }

    private async Task AddMembershipAsync(World w, Guid memberId, DateOnly start, int days)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        db.Add(Membership.Create(w.GymId, memberId, w.PlanId, start, days));
        await db.SaveChangesAsync();
    }

    private async Task<World> SeedAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var gym = Gym.Create("Status Gym", $"st-{Guid.NewGuid():N}"[..16]);
        var plan = MembershipPlan.Create(gym.Id, "Mensual", null, 50000m, 30);
        var members = Enumerable.Range(0, 5)
            .Select(i => Member.Create(gym.Id, $"M{i}", "Prueba", $"doc-{Guid.NewGuid():N}"[..12], Today.AddDays(-100)))
            .ToList();
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id), plan);
        db.AddRange(members);
        await db.SaveChangesAsync();

        return new World(gym.Id, plan.Id, members.Select(m => m.Id).ToList());
    }

    private sealed record World(Guid GymId, Guid PlanId, List<Guid> Members);
}
