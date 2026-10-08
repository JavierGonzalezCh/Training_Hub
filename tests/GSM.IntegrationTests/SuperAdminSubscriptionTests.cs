using GSM.Application.Gyms;
using GSM.Application.Gyms.Contracts;
using GSM.Application.SuperAdmin;
using GSM.Application.SuperAdmin.Contracts;
using GSM.Application.SuperAdmin.Public;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.SuperAdmin;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// Etapa 11.1: cada gym tiene su suscripción y el sistema sabe en qué estado comercial está.
/// Todavía sin bloquear nada (eso es 11.2).
/// </summary>
public sealed class SuperAdminSubscriptionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = new(2026, 9, 9);

    [Fact]
    public async Task Portfolio_lists_every_gym_with_its_effective_status()
    {
        var world = await SeedAsync();

        using var scope = SuperAdmin();
        var result = await scope.ServiceProvider.GetRequiredService<ListGymPortfolioHandler>()
            .HandleAsync(new PortfolioQuery(), default);

        Assert.Contains(result.Items, i => i.GymId == world.ActiveGym && i.EffectiveStatus == "Active");
        Assert.Contains(result.Items, i => i.GymId == world.OverdueGym && i.EffectiveStatus == "Overdue");
        Assert.Equal(1, result.CountByStatus["Active"]);
        Assert.Equal(1, result.CountByStatus["Overdue"]);

        var filtered = await scope.ServiceProvider.GetRequiredService<ListGymPortfolioHandler>()
            .HandleAsync(new PortfolioQuery { Status = "overdue" }, default);
        Assert.All(filtered.Items, i => Assert.Equal("Overdue", i.EffectiveStatus));
    }

    [Fact]
    public async Task Overdue_gym_reads_as_overdue_without_running_anything()
    {
        var world = await SeedAsync();

        using var scope = SuperAdmin();
        var reader = scope.ServiceProvider.GetRequiredService<ISubscriptionReader>();

        var view = await reader.GetStatusViewAsync(world.OverdueGym, default);
        Assert.NotNull(view);
        Assert.Equal(EffectiveSubscriptionStatus.Overdue, view!.Status);
        Assert.Equal(14, view.DaysUntilDisabled); // vencido hace 1 día, 15 de gracia
        Assert.True(await reader.IsWriteAllowedAsync(world.OverdueGym, default));
    }

    [Fact]
    public async Task A_gym_without_subscription_is_not_blocked()
    {
        var world = await SeedAsync();

        using var scope = SuperAdmin();
        var reader = scope.ServiceProvider.GetRequiredService<ISubscriptionReader>();

        Assert.Null(await reader.GetEffectiveStatusAsync(world.NoSubscriptionGym, default));
        Assert.True(await reader.IsWriteAllowedAsync(world.NoSubscriptionGym, default));
    }

    [Fact]
    public async Task Renewing_without_payment_returns_the_gym_to_active_immediately()
    {
        var world = await SeedAsync();

        using var scope = SuperAdmin();
        var detail = await scope.ServiceProvider.GetRequiredService<RenewSubscriptionHandler>()
            .HandleAsync(world.OverdueGym, new RenewSubscriptionRequest(Today.AddDays(30), "Cortesía por incidencia"), default);

        Assert.Equal("Active", detail.EffectiveStatus);
        Assert.Equal(Today.AddDays(30), detail.PaidThrough);
        Assert.Contains("Renovación sin pago", detail.Notes);
    }

    [Fact]
    public async Task Suspend_is_manual_only_and_reactivate_reverses_it()
    {
        var world = await SeedAsync();

        using (var scope = SuperAdmin())
        {
            var suspended = await scope.ServiceProvider.GetRequiredService<SuspendGymHandler>()
                .HandleAsync(world.ActiveGym, new SuspendGymRequest("Impago reiterado"), default);
            Assert.Equal("Suspended", suspended.EffectiveStatus);
            Assert.Equal("Suspended", suspended.ManualStatus);
        }

        using (var scope = SuperAdmin())
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            var status = await db.Set<Gym>().IgnoreQueryFilters()
                .Where(g => g.Id == world.ActiveGym).Select(g => g.Status).SingleAsync();
            Assert.Equal(GymStatus.Suspended, status);
        }

        using (var scope = SuperAdmin())
        {
            var reactivated = await scope.ServiceProvider.GetRequiredService<ReactivateGymHandler>()
                .HandleAsync(world.ActiveGym, default);
            Assert.Equal("Active", reactivated.EffectiveStatus);
        }
    }

    [Fact]
    public async Task Creating_a_gym_provisions_its_trial_subscription()
    {
        await SeedAsync();

        Guid gymId;
        using (var scope = SuperAdmin())
        {
            var created = await scope.ServiceProvider.GetRequiredService<CreateGymHandler>()
                .HandleAsync(new CreateGymRequest(
                    "Nuevo Gym", $"nuevo-{Guid.NewGuid():N}"[..18], null, null, null, GymPlan.Pro,
                    new CreateGymOwner($"owner-{Guid.NewGuid():N}@nuevo.local", "Nuevo Owner", "OwnerPass_123")),
                    default);
            gymId = created.GymId;
        }

        using (var scope = SuperAdmin())
        {
            var detail = await scope.ServiceProvider.GetRequiredService<GetGymDetailHandler>()
                .HandleAsync(gymId, default);

            Assert.Equal("Pro", detail.Subscription.Plan);
            Assert.Equal(Today.AddDays(30), detail.Subscription.TrialEndsOn);
            Assert.Equal(Today.AddDays(30), detail.Subscription.PaidThrough);
            Assert.Equal("Trial", detail.Subscription.EffectiveStatus);
        }
    }

    [Fact]
    public async Task Updating_the_subscription_of_a_gym_without_one_creates_it()
    {
        var world = await SeedAsync();

        using var scope = SuperAdmin();
        var detail = await scope.ServiceProvider.GetRequiredService<UpdateSubscriptionHandler>()
            .HandleAsync(world.NoSubscriptionGym, new UpdateSubscriptionRequest(GymPlan.Enterprise, 250_000m), default);

        Assert.Equal("Enterprise", detail.Plan);
        Assert.Equal(250_000m, detail.MonthlyPrice);

        // Nace sin cobertura por delante: la pone después un pago o una renovación.
        Assert.Equal(Today, detail.PaidThrough);
        Assert.NotNull(await scope.ServiceProvider.GetRequiredService<ISubscriptionReader>()
            .GetEffectiveStatusAsync(world.NoSubscriptionGym, default));
    }

    private IServiceScope SuperAdmin()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>()
            .ActAs(Guid.CreateVersion7(), gymId: null, UserRole.SuperAdmin);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Today);
        return scope;
    }

    private async Task<World> SeedAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, superadmin.gym_subscriptions restart identity cascade;");

        var active = MakeGym(db, "sa-active", gymId =>
            GymSubscription.Create(gymId, GymPlan.Basic, 100_000m, Today.AddDays(-10), Today.AddDays(20)));
        var overdue = MakeGym(db, "sa-overdue", gymId =>
            GymSubscription.Create(gymId, GymPlan.Basic, 100_000m, Today.AddDays(-40), Today.AddDays(-1)));
        var noSub = MakeGym(db, "sa-nosub", withSubscription: null);

        await db.SaveChangesAsync();
        return new World(active, overdue, noSub);
    }

    private static Guid MakeGym(
        GsmDbContext db, string slugPrefix, Func<Guid, GymSubscription>? withSubscription)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = Gym.Create(slugPrefix, $"{slugPrefix}-{suffix}");
        var owner = new GSM.Infrastructure.Identity.AppUser
        {
            UserName = $"{slugPrefix}-{suffix}@test.local",
            Email = $"{slugPrefix}-{suffix}@test.local",
            NormalizedUserName = $"{slugPrefix}-{suffix}@TEST.LOCAL".ToUpperInvariant(),
            NormalizedEmail = $"{slugPrefix}-{suffix}@TEST.LOCAL".ToUpperInvariant(),
            EmailConfirmed = true,
            FullName = $"{slugPrefix} owner",
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        };
        gym.AssignOwner(owner.Id);
        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        db.Add(owner);

        if (withSubscription is not null)
        {
            db.Add(withSubscription(gym.Id));
        }

        return gym.Id;
    }

    private sealed record World(Guid ActiveGym, Guid OverdueGym, Guid NoSubscriptionGym);
}
