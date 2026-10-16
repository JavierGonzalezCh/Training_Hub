using GSM.Application.Attendance.Public;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Logs;
using GSM.Application.Gyms.Public;
using GSM.Application.Identity.Public;
using GSM.Application.Members.Public;
using GSM.Application.Memberships.Public;
using GSM.Application.Payments.Public;
using GSM.Application.SuperAdmin;
using GSM.Application.SuperAdmin.Abstractions;
using GSM.Application.SuperAdmin.Contracts;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Memberships;
using GSM.Domain.SuperAdmin;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using AttendanceEntity = GSM.Domain.Attendance.Attendance;
using AttendanceMethodEnum = GSM.Domain.Attendance.AttendanceMethod;

namespace GSM.IntegrationTests;

/// <summary>
/// D3: los bloques nuevos del panel del SuperAdmin (Dashboards §3): requiere acción, arranque, límites, inactividad y
/// MRR. La base de pruebas se comparte con otras clases, así que cada prueba busca <b>sus</b> gyms por id y nunca
/// afirma totales de la plataforma. El "hoy" es las 20:00 de Colombia (01:00 UTC del día siguiente): si algún cálculo
/// saliera de UTC adelantaría un día.
/// </summary>
public sealed class AdminDashboardD3Tests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "OwnerPass_123";
    private static readonly DateOnly Today = new(2026, 11, 10);

    // 20:00 del 10-nov en Bogotá = 01:00 UTC del 11-nov.
    private static readonly DateTimeOffset EveningUtc = new(2026, 11, 11, 1, 0, 0, TimeSpan.Zero);

    // ---------- requiere acción ----------

    [Fact]
    public async Task Attention_lists_place_each_gym_by_its_derived_state_and_sort_by_urgency()
    {
        var trialEnd = Today.AddDays(-1); // prueba terminada ayer: 3 días de gracia, 2 hasta inhabilitarse
        var trialEnded = await SeedGymAsync(GymStatus.Trial, Today.AddDays(-20), trialEnd, trialEnd);
        var overdue = await SeedGymAsync(GymStatus.Active, Today.AddDays(-90), null, Today.AddDays(-14)); // 1 día
        var expiring = await SeedGymAsync(GymStatus.Active, Today.AddDays(-90), null, Today.AddDays(5));
        var healthy = await SeedGymAsync(GymStatus.Active, Today.AddDays(-90), null, Today.AddDays(40));

        var dash = await GetDashboardAsync();
        var attention = dash.Attention!;

        var te = attention.TrialEnded.Items.Single(i => i.GymId == trialEnded.GymId);
        Assert.Equal("TrialEnded", te.EffectiveStatus);
        Assert.Equal(2, te.DaysUntilDisabled);

        var od = attention.Overdue.Items.Single(i => i.GymId == overdue.GymId);
        Assert.Equal("Overdue", od.EffectiveStatus);
        Assert.Equal(1, od.DaysUntilDisabled);

        // Por inhabilitarse: 3 días o menos. Entran los dos; el sano y el que solo vence, no.
        Assert.Contains(attention.AboutToDisable.Items, i => i.GymId == trialEnded.GymId);
        Assert.Contains(attention.AboutToDisable.Items, i => i.GymId == overdue.GymId);
        Assert.DoesNotContain(attention.AboutToDisable.Items, i => i.GymId == expiring.GymId);

        var ex = attention.ExpiringSoon.Items.Single(i => i.GymId == expiring.GymId);
        Assert.Equal(5, ex.DaysUntilExpiry);
        Assert.Null(ex.DaysUntilDisabled);
        Assert.DoesNotContain(attention.ExpiringSoon.Items, i => i.GymId == healthy.GymId);
        Assert.DoesNotContain(attention.Overdue.Items, i => i.GymId == healthy.GymId);

        // Orden: el que se inhabilita antes, primero.
        var aboutIds = attention.AboutToDisable.Items.Select(i => i.GymId).ToList();
        Assert.True(aboutIds.IndexOf(overdue.GymId) < aboutIds.IndexOf(trialEnded.GymId));
        Assert.True(attention.TrialEnded.Total >= 1);
        Assert.True(attention.TrialEnded.Items.Count <= GetAdminDashboardHandler.ListLimit);
    }

    // ---------- inactividad ----------

    [Fact]
    public async Task A_live_gym_with_seven_days_without_entries_is_flagged_inactive_in_the_gyms_time_zone()
    {
        var seven = await SeedGymAsync(GymStatus.Active, Today.AddDays(-60), null, Today.AddDays(20));
        var six = await SeedGymAsync(GymStatus.Active, Today.AddDays(-60), null, Today.AddDays(20));
        var never = await SeedGymAsync(GymStatus.Active, Today.AddDays(-60), null, Today.AddDays(20));
        var brandNew = await SeedGymAsync(GymStatus.Active, Today.AddDays(-3), null, Today.AddDays(27));
        var suspended = await SeedGymAsync(GymStatus.Suspended, Today.AddDays(-60), null, Today.AddDays(20));

        await AddAttendanceAsync(seven.GymId, Today.AddDays(-7));
        await AddAttendanceAsync(six.GymId, Today.AddDays(-6));

        var dash = await GetDashboardAsync();
        UsageOf(dash, seven.GymId, out var sevenRow);
        UsageOf(dash, six.GymId, out var sixRow);
        UsageOf(dash, never.GymId, out var neverRow);
        UsageOf(dash, brandNew.GymId, out var newRow);
        UsageOf(dash, suspended.GymId, out var suspendedRow);

        Assert.True(sevenRow.IsInactive);
        Assert.Equal(7, sevenRow.DaysSinceLastCheckIn);
        Assert.False(sixRow.IsInactive);
        Assert.Equal(6, sixRow.DaysSinceLastCheckIn);
        Assert.True(neverRow.IsInactive);
        Assert.Null(neverRow.DaysSinceLastCheckIn);
        Assert.False(newRow.IsInactive);       // recién creado: aún no es una alerta de uso
        Assert.False(suspendedRow.IsInactive); // suspendido: es otro caso del panel
    }

    // ---------- arranque ----------

    [Fact]
    public async Task Startup_lists_gyms_with_an_unactivated_owner_or_an_unfinished_onboarding()
    {
        var invited = await SeedGymAsync(GymStatus.Active, Today.AddDays(-5), null, Today.AddDays(25), ownerWithPassword: false);
        var onboardingOnly = await SeedGymAsync(GymStatus.Active, Today.AddDays(-5), null, Today.AddDays(25), ownerWithPassword: true);
        var finished = await SeedGymAsync(GymStatus.Active, Today.AddDays(-5), null, Today.AddDays(25), ownerWithPassword: true);
        var suspended = await SeedGymAsync(GymStatus.Suspended, Today.AddDays(-5), null, Today.AddDays(25), ownerWithPassword: false);
        await CompleteOnboardingAsync(finished.GymId);

        var dash = await GetDashboardAsync();
        var startup = dash.Startup!;

        var inv = startup.Items.Single(i => i.GymId == invited.GymId);
        Assert.True(inv.OwnerNotActivated);
        Assert.True(inv.OnboardingPending);
        Assert.Equal(invited.OwnerId, inv.PendingOwnerUserId);
        Assert.Equal(invited.OwnerEmail, inv.PendingOwnerEmail);
        Assert.True(inv.DaysSinceCreated >= 0);

        var onb = startup.Items.Single(i => i.GymId == onboardingOnly.GymId);
        Assert.False(onb.OwnerNotActivated);
        Assert.True(onb.OnboardingPending);
        Assert.Null(onb.PendingOwnerUserId);

        Assert.DoesNotContain(startup.Items, i => i.GymId == finished.GymId);
        Assert.DoesNotContain(startup.Items, i => i.GymId == suspended.GymId);
        Assert.True(startup.Total >= startup.Items.Count);
    }

    // ---------- límites ----------

    [Fact]
    public async Task Limits_lists_only_gyms_at_90_percent_or_more_of_their_member_limit()
    {
        var atNinety = await SeedGymAsync(GymStatus.Active, Today.AddDays(-60), null, Today.AddDays(20), memberLimit: 10);
        var atEighty = await SeedGymAsync(GymStatus.Active, Today.AddDays(-60), null, Today.AddDays(20), memberLimit: 10);
        var full = await SeedGymAsync(GymStatus.Active, Today.AddDays(-60), null, Today.AddDays(20), memberLimit: 4);
        var unlimited = await SeedGymAsync(GymStatus.Active, Today.AddDays(-60), null, Today.AddDays(20), memberLimit: null, plan: GymPlan.Enterprise);
        await AddCoveredMembersAsync(atNinety.GymId, 9);
        await AddCoveredMembersAsync(atEighty.GymId, 8);
        await AddCoveredMembersAsync(full.GymId, 4);
        await AddCoveredMembersAsync(unlimited.GymId, 12);

        var dash = await GetDashboardAsync();
        var limits = dash.Limits!;

        var ninety = limits.Items.Single(i => i.GymId == atNinety.GymId);
        Assert.Equal(9, ninety.CountedMembers);
        Assert.Equal(10, ninety.MemberLimit);
        Assert.Equal(90, ninety.UsagePercent);
        Assert.Equal(100, limits.Items.Single(i => i.GymId == full.GymId).UsagePercent);
        Assert.DoesNotContain(limits.Items, i => i.GymId == atEighty.GymId);
        Assert.DoesNotContain(limits.Items, i => i.GymId == unlimited.GymId);

        // El uso por gym trae el mismo "9 / 10".
        UsageOf(dash, atNinety.GymId, out var usage);
        Assert.Equal(9, usage.CountedMembers);
        Assert.Equal(10, usage.MemberLimit);
        UsageOf(dash, unlimited.GymId, out var unlimitedUsage);
        Assert.Null(unlimitedUsage.MemberLimit);
    }

    // ---------- ingresos ----------

    [Fact]
    public async Task Mrr_counts_active_and_overdue_subscriptions_and_pending_is_never_negative()
    {
        await SeedGymAsync(GymStatus.Active, Today.AddDays(-60), null, Today.AddDays(20));

        var dash = await GetDashboardAsync();

        Assert.True(dash.Revenue.Mrr >= 100_000m);
        Assert.True(dash.Revenue.Pending >= 0m);
        // El MRR (al día + en mora) nunca supera lo contratado (que además suma pruebas y pruebas terminadas).
        Assert.True(dash.Revenue.Mrr <= dash.Revenue.Contracted);
    }

    // ---------- un bloque que falla no tumba el panel ----------

    [Fact]
    public async Task A_failing_reader_leaves_its_block_null_and_the_rest_of_the_panel_arrives()
    {
        var gym = await SeedGymAsync(GymStatus.Active, Today.AddDays(-60), null, Today.AddDays(2));

        using var scope = SuperAdminScope();
        var sp = scope.ServiceProvider;
        var handler = new GetAdminDashboardHandler(
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<IGymSubscriptionRepository>(),
            sp.GetRequiredService<ISubscriptionPaymentRepository>(),
            sp.GetRequiredService<ISaasPlanRepository>(),
            sp.GetRequiredService<IMemberReader>(),
            sp.GetRequiredService<IAttendanceReader>(),
            sp.GetRequiredService<IPaymentReader>(),
            sp.GetRequiredService<IMembershipReader>(),
            sp.GetRequiredService<IGymReader>(),
            new ThrowingIdentityReader(),
            sp.GetRequiredService<ILogStore>());

        var dash = await handler.HandleAsync(default);

        Assert.Null(dash.Startup);
        Assert.NotNull(dash.Attention);
        Assert.NotNull(dash.Limits);
        Assert.Contains(dash.GymUsage, u => u.GymId == gym.GymId);
        Assert.Contains(dash.Attention!.ExpiringSoon.Items, i => i.GymId == gym.GymId);
    }

    // ---------- ayudas ----------

    private static void UsageOf(AdminDashboardResponse dash, Guid gymId, out GymUsageRow row)
        => row = dash.GymUsage.Single(u => u.GymId == gymId);

    private async Task<AdminDashboardResponse> GetDashboardAsync()
    {
        using var scope = SuperAdminScope();
        return await scope.ServiceProvider.GetRequiredService<GetAdminDashboardHandler>().HandleAsync(default);
    }

    private IServiceScope SuperAdminScope()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinTo(EveningUtc, Today);
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(Guid.CreateVersion7(), gymId: null, UserRole.SuperAdmin);
        return scope;
    }

    private IServiceScope SystemScope()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinTo(EveningUtc, Today);
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        return scope;
    }

    private async Task AddAttendanceAsync(Guid gymId, DateOnly day)
    {
        using var scope = SystemScope();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var member = Member.Create(gymId, "Ana", "Uso", $"doc-{Guid.NewGuid():N}"[..12], Today.AddDays(-100));
        db.Add(member);
        db.Add(AttendanceEntity.Create(
            gymId, member.Id, AttendanceMethodEnum.Manual,
            new DateTimeOffset(day.Year, day.Month, day.Day, 15, 0, 0, TimeSpan.Zero), day));
        await db.SaveChangesAsync();
    }

    private async Task AddCoveredMembersAsync(Guid gymId, int count)
    {
        using var scope = SystemScope();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var plan = MembershipPlan.Create(gymId, "Mensual", null, 50_000m, 30);
        db.Add(plan);
        for (var i = 0; i < count; i++)
        {
            var member = Member.Create(gymId, "Miembro", $"{i}", $"doc-{Guid.NewGuid():N}"[..12], Today.AddDays(-60));
            db.Add(member);
            db.Add(Membership.Create(gymId, member.Id, plan.Id, Today.AddDays(-5), 60));
        }

        await db.SaveChangesAsync();
    }

    private async Task CompleteOnboardingAsync(Guid gymId)
    {
        using var scope = SystemScope();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var onboarding = GymOnboarding.Start(gymId);
        onboarding.Complete(EveningUtc);
        db.Add(onboarding);
        await db.SaveChangesAsync();
    }

    private async Task<World> SeedGymAsync(
        GymStatus status, DateOnly startedOn, DateOnly? trialEndsOn, DateOnly paidThrough,
        bool ownerWithPassword = true, int? memberLimit = null, GymPlan plan = GymPlan.Basic)
    {
        using var scope = SystemScope();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = Gym.Create("D3 Gym", $"d3-{suffix}");
        if (status != GymStatus.Trial)
        {
            gym.SetCommercialStatus(status);
        }

        var subscription = GymSubscription.Create(gym.Id, plan, 100_000m, startedOn, paidThrough, trialEndsOn);
        if (memberLimit is { } limit)
        {
            subscription.SetMemberLimitOverride(limit);
        }

        db.AddRange(gym, GymFeatureFlags.Default(gym.Id), subscription);
        await db.SaveChangesAsync();

        var provisioning = scope.ServiceProvider.GetRequiredService<IUserProvisioning>();
        var email = $"d3-owner-{suffix}@test.local";
        Guid ownerId;
        if (ownerWithPassword)
        {
            ownerId = await provisioning.CreateGymUserAsync(email, "Owner D3", Password, UserRole.Owner, gym.Id);
        }
        else
        {
            ownerId = (await provisioning.InviteGymOwnerAsync(email, "Owner D3", gym.Id)).UserId;
        }

        gym.AssignOwner(ownerId);
        await db.SaveChangesAsync();
        return new World(gym.Id, ownerId, email);
    }

    private sealed record World(Guid GymId, Guid OwnerId, string OwnerEmail);

    /// <summary>Un lector de Identity que falla al pedir los Owners sin activar: simula la falla de un bloque.</summary>
    private sealed class ThrowingIdentityReader : IIdentityReader
    {
        public Task<IdentityUserDto?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("falla simulada");

        public Task<IReadOnlyList<TrainerCandidateDto>> ListTrainersAsync(Guid gymId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("falla simulada");

        public Task<bool> CanBeTrainerAsync(Guid userId, Guid gymId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("falla simulada");

        public Task<IReadOnlySet<Guid>> ListUserIdsWithAnyPermissionAsync(
            Guid gymId, IReadOnlyCollection<StaffPermission> permissions, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("falla simulada");

        public Task<IReadOnlyList<GymOwnerDto>> ListActiveOwnersAsync(Guid gymId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("falla simulada");

        public Task<GymTeamSetup> GetTeamSetupAsync(Guid gymId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("falla simulada");

        public Task<SuperAdminContactDto?> GetSuperAdminContactAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("falla simulada");

        public Task<IReadOnlyList<PendingOwnerDto>> ListOwnersPendingActivationAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("falla simulada");
    }
}
