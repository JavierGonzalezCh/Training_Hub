using GSM.API.Filters;
using GSM.Application.Gyms;
using GSM.Application.Identity;
using GSM.Application.Identity.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.SuperAdmin;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// Etapa 11.2: el estado comercial tiene consecuencias reales. La decisión del bloqueo es pura
/// (<see cref="SubscriptionGate.Evaluate"/>) y se prueba exhaustivamente; el resto verifica los dos
/// puntos que el filtro no cubre por sí solo: el login (anónimo) y el bloque <c>subscription</c>.
/// </summary>
public sealed class SubscriptionGateTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = new(2026, 9, 9);

    // --- Decisión pura -----------------------------------------------------------------------

    [Theory]
    [InlineData(null, false)]                                        // sin suscripción → pasa (incluso escritura)
    [InlineData(EffectiveSubscriptionStatus.Active, false)]
    [InlineData(EffectiveSubscriptionStatus.Trial, false)]
    [InlineData(EffectiveSubscriptionStatus.Overdue, false)]         // en mora no bloquea nada
    [InlineData(EffectiveSubscriptionStatus.Overdue, true)]
    [InlineData(EffectiveSubscriptionStatus.Disabled, true)]         // disabled + lectura → pasa
    public void Allowed_cases(EffectiveSubscriptionStatus? status, bool isRead)
        => Assert.Equal(
            SubscriptionGate.Decision.Allow,
            SubscriptionGate.Evaluate(status, isSuperAdmin: false, hasGym: true, isRead, isExempt: false));

    [Fact]
    public void Disabled_blocks_writes_only()
    {
        Assert.Equal(
            SubscriptionGate.Decision.BlockedDisabled,
            SubscriptionGate.Evaluate(EffectiveSubscriptionStatus.Disabled, false, true, isReadMethod: false, false));
        Assert.Equal(
            SubscriptionGate.Decision.Allow,
            SubscriptionGate.Evaluate(EffectiveSubscriptionStatus.Disabled, false, true, isReadMethod: true, false));
    }

    [Fact]
    public void Suspended_blocks_everything_including_reads()
    {
        Assert.Equal(
            SubscriptionGate.Decision.BlockedSuspended,
            SubscriptionGate.Evaluate(EffectiveSubscriptionStatus.Suspended, false, true, isReadMethod: true, false));
    }

    [Fact]
    public void SuperAdmin_and_exempt_endpoints_are_never_blocked()
    {
        Assert.Equal(
            SubscriptionGate.Decision.Allow,
            SubscriptionGate.Evaluate(EffectiveSubscriptionStatus.Suspended, isSuperAdmin: true, true, false, false));
        Assert.Equal(
            SubscriptionGate.Decision.Allow,
            SubscriptionGate.Evaluate(EffectiveSubscriptionStatus.Suspended, false, true, false, isExempt: true));
    }

    [Fact]
    public void Request_without_gym_context_is_not_evaluated()
    {
        Assert.Equal(
            SubscriptionGate.Decision.Allow,
            SubscriptionGate.Evaluate(EffectiveSubscriptionStatus.Disabled, false, hasGym: false, false, false));
    }

    // --- Login: un gym suspendido no deja entrar ni a sus usuarios --------------------------

    [Fact]
    public async Task Suspended_gym_cannot_log_in()
    {
        var w = await SeedAsync(Today.AddDays(20), GymStatus.Suspended, password: "OwnerPass_123");

        using var scope = Scope();
        var login = scope.ServiceProvider.GetRequiredService<LoginHandler>();

        await Assert.ThrowsAsync<ForbiddenDomainException>(() =>
            login.HandleAsync(new LoginRequest(w.OwnerEmail, "OwnerPass_123"), default));
    }

    [Fact]
    public async Task Disabled_gym_can_still_log_in()
    {
        var w = await SeedAsync(Today.AddDays(-30), GymStatus.Active, password: "OwnerPass_123");

        using var scope = Scope();
        var tokens = await scope.ServiceProvider.GetRequiredService<LoginHandler>()
            .HandleAsync(new LoginRequest(w.OwnerEmail, "OwnerPass_123"), default);

        Assert.False(string.IsNullOrEmpty(tokens.AccessToken));
    }

    // --- El bloque `subscription` en GET /api/gym ------------------------------------------

    [Fact]
    public async Task Gym_response_carries_the_subscription_block_with_days_until_disabled()
    {
        var w = await SeedAsync(Today.AddDays(-3), GymStatus.Active);

        using var scope = Scope();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(w.OwnerId, w.GymId, UserRole.Owner);

        var gym = await scope.ServiceProvider.GetRequiredService<GetGymHandler>().HandleAsync(default);

        Assert.NotNull(gym.Subscription);
        Assert.Equal("Overdue", gym.Subscription!.Status);
        Assert.Equal(12, gym.Subscription.DaysUntilDisabled);
    }

    [Fact]
    public async Task Gym_without_subscription_gets_a_null_block()
    {
        var w = await SeedAsync(paidThrough: null, GymStatus.Active);

        using var scope = Scope();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(w.OwnerId, w.GymId, UserRole.Owner);

        var gym = await scope.ServiceProvider.GetRequiredService<GetGymHandler>().HandleAsync(default);
        Assert.Null(gym.Subscription);
    }

    private IServiceScope Scope()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Today);
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        return scope;
    }

    private async Task<World> SeedAsync(DateOnly? paidThrough, GymStatus manual, string? password = null)
    {
        using var scope = Scope();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, superadmin.gym_subscriptions restart identity cascade;");

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"gate-{suffix}@test.local";
        var gym = Gym.Create("Gate Gym", $"gate-{suffix}");
        if (manual != GymStatus.Trial)
        {
            gym.SetCommercialStatus(manual);
        }

        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        if (paidThrough is { } pt)
        {
            db.Add(GymSubscription.Create(gym.Id, GymPlan.Basic, 100_000m, Today.AddDays(-40), pt));
        }

        await db.SaveChangesAsync();

        Guid ownerId;
        if (password is not null)
        {
            ownerId = await scope.ServiceProvider
                .GetRequiredService<GSM.Application.Identity.Public.IUserProvisioning>()
                .CreateGymUserAsync(email, "Gate Owner", password, UserRole.Owner, gym.Id);
        }
        else
        {
            var owner = new GSM.Infrastructure.Identity.AppUser
            {
                UserName = email,
                Email = email,
                NormalizedUserName = email.ToUpperInvariant(),
                NormalizedEmail = email.ToUpperInvariant(),
                EmailConfirmed = true,
                FullName = "Gate Owner",
                Role = UserRole.Owner,
                GymId = gym.Id,
                IsActive = true,
                SecurityStamp = Guid.NewGuid().ToString(),
            };
            db.Add(owner);
            await db.SaveChangesAsync();
            ownerId = owner.Id;
        }

        gym.AssignOwner(ownerId);
        await db.SaveChangesAsync();

        return new World(gym.Id, ownerId, email);
    }

    private sealed record World(Guid GymId, Guid OwnerId, string OwnerEmail);
}
