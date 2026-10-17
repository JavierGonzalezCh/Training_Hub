using GSM.Application.SuperAdmin;
using GSM.Application.SuperAdmin.Abstractions;
using GSM.Application.SuperAdmin.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Payments;
using GSM.Domain.SuperAdmin;
using GSM.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>S3-3: el Owner lee la suscripción de su gym (solo lectura): plan, estado, motivo de suspensión, uso y pagos.</summary>
public sealed class OwnSubscriptionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = new(2026, 10, 17);

    [Fact]
    public async Task The_owner_sees_plan_status_usage_and_the_latest_payments()
    {
        var gymId = await SeedAsync(paidThrough: Today.AddDays(20), withPlan: true, payments: 14);

        using var scope = Owner(gymId);
        var view = await scope.ServiceProvider.GetRequiredService<GetOwnSubscriptionHandler>().HandleAsync(default);

        Assert.Equal("Pro", view.Plan);
        Assert.Equal(200_000m, view.MonthlyPrice);
        Assert.Equal("Active", view.EffectiveStatus);
        Assert.Equal(Today.AddDays(20), view.PaidThrough);
        Assert.Equal(500, view.MemberLimit);
        Assert.Equal(0, view.CountedMembers);
        Assert.Null(view.SuspensionReason);
        Assert.Equal(GetOwnSubscriptionHandler.RecentPaymentsCount, view.RecentPayments.Count);
        Assert.Equal(
            view.RecentPayments.OrderByDescending(p => p.CoversThrough).Select(p => p.Id),
            view.RecentPayments.Select(p => p.Id));
    }

    [Fact]
    public async Task A_gym_without_a_chosen_plan_reads_as_no_plan_and_no_limit()
    {
        var gymId = await SeedAsync(paidThrough: Today.AddDays(5), withPlan: false, payments: 0);

        using var scope = Owner(gymId);
        var view = await scope.ServiceProvider.GetRequiredService<GetOwnSubscriptionHandler>().HandleAsync(default);

        Assert.Equal("Sin plan", view.Plan);
        Assert.Null(view.MemberLimit);
        Assert.Empty(view.RecentPayments);
    }

    [Fact]
    public async Task A_suspended_gym_shows_the_reason_but_never_the_internal_notes()
    {
        var gymId = await SeedAsync(paidThrough: Today.AddDays(20), withPlan: true, payments: 0);

        using (var scope = SuperAdmin())
        {
            await scope.ServiceProvider.GetRequiredService<SuspendGymHandler>()
                .HandleAsync(gymId, new SuspendGymRequest("Cobro pendiente"), default);
        }

        using var owner = Owner(gymId);
        var view = await owner.ServiceProvider.GetRequiredService<GetOwnSubscriptionHandler>().HandleAsync(default);

        Assert.Equal("Suspended", view.EffectiveStatus);
        Assert.Equal("Cobro pendiente", view.SuspensionReason);
        Assert.DoesNotContain("por ", view.SuspensionReason!);
    }

    [Fact]
    public async Task A_gym_without_subscription_gets_404()
    {
        Guid gymId;
        using (var scope = factory.CreateScopeWithMigration())
        {
            scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            var gym = Gym.Create("Sin Suscripcion", $"ss-{Guid.NewGuid():N}"[..16]);
            db.Add(gym);
            db.Add(GymFeatureFlags.Default(gym.Id));
            await db.SaveChangesAsync();
            gymId = gym.Id;
        }

        using var owner = Owner(gymId);
        await Assert.ThrowsAsync<NotFoundException>(
            () => owner.ServiceProvider.GetRequiredService<GetOwnSubscriptionHandler>().HandleAsync(default));
    }

    private async Task<Guid> SeedAsync(DateOnly paidThrough, bool withPlan, int payments)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var gym = Gym.Create("Own Sub Gym", $"os-{Guid.NewGuid():N}"[..16]);
        gym.SetCommercialStatus(GymStatus.Active);
        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));

        var sub = GymSubscription.Create(gym.Id, 200_000m, Today.AddDays(-400), paidThrough);
        if (withPlan)
        {
            var pro = await scope.ServiceProvider.GetRequiredService<ISaasPlanRepository>().GetByNameAsync("Pro", default);
            sub.AssignSaasPlan(pro!.Id);
        }

        db.Add(sub);

        var admin = Guid.CreateVersion7();
        for (var i = 0; i < payments; i++)
        {
            var from = Today.AddDays(-30 * (i + 1));
            db.Add(SubscriptionPayment.Create(
                gym.Id, 200_000m, PaymentMethod.BankTransfer, from, from.AddDays(29),
                new DateTimeOffset(from.Year, from.Month, from.Day, 15, 0, 0, TimeSpan.Zero), admin, null, null));
        }

        await db.SaveChangesAsync();
        return gym.Id;
    }

    private IServiceScope Owner(Guid gymId)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(Guid.CreateVersion7(), gymId, UserRole.Owner);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Today);
        return scope;
    }

    private IServiceScope SuperAdmin()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>()
            .ActAs(Guid.CreateVersion7(), gymId: null, UserRole.SuperAdmin);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Today);
        return scope;
    }
}
