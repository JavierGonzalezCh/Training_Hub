using GSM.Application.SuperAdmin;
using GSM.Application.SuperAdmin.Abstractions;
using GSM.Application.SuperAdmin.Contracts;
using GSM.Application.SuperAdmin.Public;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Payments;
using GSM.Domain.SuperAdmin;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// Etapa 11.3: registrar un pago del gym al SaaS lo renueva solo, de forma atómica.
/// </summary>
public sealed class SubscriptionPaymentTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = new(2026, 9, 9);

    [Fact]
    public async Task Registering_a_payment_pushes_paid_through_and_returns_the_gym_to_active()
    {
        var w = await SeedAsync(paidThrough: Today.AddDays(-5)); // overdue

        RegisterSubscriptionPaymentResponse result;
        using (var scope = Admin(w))
        {
            result = await scope.ServiceProvider.GetRequiredService<RegisterSubscriptionPaymentHandler>()
                .HandleAsync(w.GymId, new RegisterSubscriptionPaymentRequest(
                    150_000m, PaymentMethod.Transfer,
                    CoversFrom: Today.AddDays(-4), CoversThrough: Today.AddDays(25),
                    Reference: "TRF-9911", Notes: null), default);
        }

        Assert.Equal("Active", result.Subscription.EffectiveStatus);
        Assert.Equal(Today.AddDays(25), result.Subscription.PaidThrough);

        using var check = Admin(w);
        var status = await check.ServiceProvider.GetRequiredService<ISubscriptionReader>()
            .GetStatusViewAsync(w.GymId, default);
        Assert.Equal(EffectiveSubscriptionStatus.Active, status!.Status);
        Assert.Equal(Today.AddDays(25), status.PaidThrough);
    }

    [Fact]
    public async Task Default_period_starts_the_day_after_the_current_paid_through_and_lasts_a_month()
    {
        var w = await SeedAsync(paidThrough: Today.AddDays(10));

        using var scope = Admin(w);
        var result = await scope.ServiceProvider.GetRequiredService<RegisterSubscriptionPaymentHandler>()
            .HandleAsync(w.GymId, new RegisterSubscriptionPaymentRequest(
                100_000m, PaymentMethod.Cash, null, null, null, null), default);

        Assert.Equal(Today.AddDays(11), result.Payment.CoversFrom);
        Assert.Equal(Today.AddDays(11).AddMonths(1).AddDays(-1), result.Payment.CoversThrough);
        Assert.Equal(result.Payment.CoversThrough, result.Subscription.PaidThrough);
    }

    [Fact]
    public async Task A_zero_payment_is_rejected_and_a_negative_adjustment_is_accepted()
    {
        var w = await SeedAsync(paidThrough: Today.AddDays(10));

        using (var scope = Admin(w))
        {
            await Assert.ThrowsAsync<ValidationDomainException>(() =>
                scope.ServiceProvider.GetRequiredService<RegisterSubscriptionPaymentHandler>()
                    .HandleAsync(w.GymId, new RegisterSubscriptionPaymentRequest(
                        0m, PaymentMethod.Cash, null, null, null, null), default));
        }

        using (var scope = Admin(w))
        {
            var result = await scope.ServiceProvider.GetRequiredService<RegisterSubscriptionPaymentHandler>()
                .HandleAsync(w.GymId, new RegisterSubscriptionPaymentRequest(
                    -30_000m, PaymentMethod.Other,
                    CoversFrom: Today, CoversThrough: Today,
                    Reference: null, Notes: "Ajuste por sobrecargo"), default);
            Assert.Equal(-30_000m, result.Payment.Amount);
        }
    }

    [Fact]
    public async Task If_the_payment_save_fails_paid_through_does_not_move()
    {
        var w = await SeedAsync(paidThrough: Today.AddDays(3));

        using var broken = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<ISubscriptionPaymentRepository>();
            s.AddScoped<ISubscriptionPaymentRepository, ThrowingPaymentRepository>();
        }));
        using (var scope = broken.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<GsmDbContext>().Database.Migrate();
            scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Today);
            scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(w.AdminId, null, UserRole.SuperAdmin);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                scope.ServiceProvider.GetRequiredService<RegisterSubscriptionPaymentHandler>()
                    .HandleAsync(w.GymId, new RegisterSubscriptionPaymentRequest(
                        200_000m, PaymentMethod.Cash, null, null, null, null), default));
        }

        using var check = Admin(w);
        var status = await check.ServiceProvider.GetRequiredService<ISubscriptionReader>()
            .GetStatusViewAsync(w.GymId, default);
        Assert.Equal(Today.AddDays(3), status!.PaidThrough); // intacto
    }

    [Fact]
    public async Task History_lists_the_gym_payments_and_the_platform_range()
    {
        var w = await SeedAsync(paidThrough: Today.AddDays(1));

        using (var scope = Admin(w))
        {
            var handler = scope.ServiceProvider.GetRequiredService<RegisterSubscriptionPaymentHandler>();
            await handler.HandleAsync(w.GymId, new RegisterSubscriptionPaymentRequest(
                100_000m, PaymentMethod.Cash, null, null, null, null), default);
            await handler.HandleAsync(w.GymId, new RegisterSubscriptionPaymentRequest(
                100_000m, PaymentMethod.Transfer, null, null, null, null), default);
        }

        using var scope2 = Admin(w);
        var byGym = await scope2.ServiceProvider.GetRequiredService<ListGymSubscriptionPaymentsHandler>()
            .HandleAsync(w.GymId, default);
        Assert.Equal(2, byGym.Count);

        var platform = await scope2.ServiceProvider.GetRequiredService<ListPlatformPaymentsHandler>()
            .HandleAsync(Today.AddDays(-1), Today.AddDays(1), default);
        Assert.Equal(2, platform.Items.Count);
        Assert.Equal(200_000m, platform.Total);
        Assert.All(platform.Items, i => Assert.Equal("Pay Gym", i.GymName));
    }

    private IServiceScope Admin(World w)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Today);
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(w.AdminId, null, UserRole.SuperAdmin);
        return scope;
    }

    private async Task<World> SeedAsync(DateOnly paidThrough)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Today);
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, superadmin.gym_subscriptions, superadmin.subscription_payments restart identity cascade;");

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = Gym.Create("Pay Gym", $"pay-{suffix}");
        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        db.Add(GymSubscription.Create(gym.Id, GymPlan.Pro, 200_000m, Today.AddDays(-40), paidThrough));
        await db.SaveChangesAsync();

        return new World(gym.Id, Guid.CreateVersion7());
    }

    private sealed record World(Guid GymId, Guid AdminId);

    private sealed class ThrowingPaymentRepository : ISubscriptionPaymentRepository
    {
        public void Add(SubscriptionPayment payment)
            => throw new InvalidOperationException("fallo simulado al guardar el pago");

        public Task<IReadOnlyList<SubscriptionPaymentRow>> GetByGymAsync(Guid gymId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<PlatformPaymentRow>> GetInRangeAsync(
            DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<decimal> SumInRangeAsync(
            DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
