using GSM.Application.Gyms;
using GSM.Application.Gyms.Contracts;
using GSM.Application.Gyms.Validators;
using GSM.Application.Identity;
using GSM.Application.Identity.Abstractions;
using GSM.Application.SuperAdmin;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Payments;
using GSM.Domain.SuperAdmin;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// G1 + S1: el SuperAdmin da de alta un gym sin inventar la contraseña del Owner (recibe un enlace de
/// activación) y elige entre prueba con días o primer pago.
/// </summary>
public sealed class GymCreationInvitationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = new(2026, 9, 9);

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..18];

    private static CreateGymRequest Request(
        string ownerEmail, int? trialDays = null, CreateGymFirstPayment? payment = null, decimal? price = null)
        => new(
            "Gym Alta", Unique("alta"), null, null, null, null,
            new CreateGymOwner(ownerEmail, "Dueño Nuevo"),
            TrialDays: trialDays, MonthlyPrice: price, FirstPayment: payment);

    private IServiceScope SuperAdmin(Guid? actorId = null)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>()
            .ActAs(actorId ?? Guid.CreateVersion7(), gymId: null, UserRole.SuperAdmin);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Today);
        return scope;
    }

    [Fact]
    public async Task The_owner_is_created_without_a_password_and_gets_an_activation_link_by_email()
    {
        var email = $"{Unique("own")}@alta.local";
        using var scope = SuperAdmin();
        var sender = scope.ServiceProvider.GetRequiredService<CapturingEmailSender>();

        var created = await scope.ServiceProvider.GetRequiredService<CreateGymHandler>()
            .HandleAsync(Request(email, trialDays: 15), default);

        Assert.True(created.OwnerInvitationSent);
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var owner = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == created.OwnerId);
        Assert.Null(owner.PasswordHash);

        var link = EmailLinks.Parse(sender.LastTo(email)!);
        Assert.Equal("/activar", link.Path);
        Assert.Equal(email, link.Email);
        Assert.False(string.IsNullOrEmpty(link.Code));

        // El enlace crea la contraseña y la cuenta queda activada.
        var account = await scope.ServiceProvider.GetRequiredService<IPasswordResetService>()
            .ActivateAsync(link.Email, link.Code!, "OwnerNew_12345");
        Assert.Equal(created.OwnerId, account.Id);
    }

    [Fact]
    public async Task The_super_admin_sees_the_new_owner_as_pending_and_resending_voids_the_previous_link()
    {
        var email = $"{Unique("own")}@alta.local";
        using var scope = SuperAdmin();
        var sender = scope.ServiceProvider.GetRequiredService<CapturingEmailSender>();
        var created = await scope.ServiceProvider.GetRequiredService<CreateGymHandler>()
            .HandleAsync(Request(email, trialDays: 30), default);
        var first = EmailLinks.Parse(sender.LastTo(email)!);

        var owners = await scope.ServiceProvider.GetRequiredService<ListGymOwnersHandler>().HandleAsync(created.GymId, default);
        Assert.Contains(owners, o => o.UserId == created.OwnerId && !o.IsActivated);

        await scope.ServiceProvider.GetRequiredService<ResendOwnerAccessHandler>()
            .HandleAsync(created.GymId, created.OwnerId, default);
        var second = EmailLinks.Parse(sender.LastTo(email)!);
        Assert.NotEqual(first.Code, second.Code);

        var reset = scope.ServiceProvider.GetRequiredService<IPasswordResetService>();
        await Assert.ThrowsAnyAsync<DomainException>(() => reset.ActivateAsync(email, first.Code!, "OwnerNew_12345"));
        await reset.ActivateAsync(email, second.Code!, "OwnerNew_12345");
    }

    [Fact]
    public async Task With_trial_the_subscription_lasts_the_days_the_super_admin_chose()
    {
        using var scope = SuperAdmin();
        var created = await scope.ServiceProvider.GetRequiredService<CreateGymHandler>()
            .HandleAsync(Request($"{Unique("own")}@alta.local", trialDays: 15, price: 120_000m), default);

        var detail = await scope.ServiceProvider.GetRequiredService<GetGymDetailHandler>().HandleAsync(created.GymId, default);

        Assert.Equal(Today.AddDays(15), detail.Subscription.TrialEndsOn);
        Assert.Equal(Today.AddDays(15), detail.Subscription.PaidThrough);
        Assert.Equal("Trial", detail.Subscription.EffectiveStatus);
        Assert.Equal(120_000m, detail.Subscription.MonthlyPrice);

        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        Assert.Equal(0, await db.Set<SubscriptionPayment>().IgnoreQueryFilters().CountAsync(p => p.GymId == created.GymId));
    }

    [Fact]
    public async Task Without_trial_the_first_payment_is_registered_and_the_gym_is_born_active()
    {
        var actor = Guid.CreateVersion7();
        using var scope = SuperAdmin(actor);
        var payment = new CreateGymFirstPayment(
            150_000m, PaymentMethod.BankTransfer, CoversFrom: null, CoversThrough: null, Reference: "TRX-1", Notes: null);

        var created = await scope.ServiceProvider.GetRequiredService<CreateGymHandler>()
            .HandleAsync(Request($"{Unique("own")}@alta.local", payment: payment, price: 150_000m), default);

        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var gym = await db.Set<Gym>().IgnoreQueryFilters().SingleAsync(g => g.Id == created.GymId);
        Assert.Equal(GymStatus.Active, gym.Status);

        var detail = await scope.ServiceProvider.GetRequiredService<GetGymDetailHandler>().HandleAsync(created.GymId, default);
        Assert.Null(detail.Subscription.TrialEndsOn);
        Assert.Equal(Today.AddMonths(1).AddDays(-1), detail.Subscription.PaidThrough);
        Assert.Equal("Active", detail.Subscription.EffectiveStatus);

        // El pago es del SaaS (superadmin.subscription_payments), firmado por el SuperAdmin que dio el alta.
        var saved = await db.Set<SubscriptionPayment>().IgnoreQueryFilters().SingleAsync(p => p.GymId == created.GymId);
        Assert.Equal(150_000m, saved.Amount);
        Assert.Equal(PaymentMethod.BankTransfer, saved.Method);
        Assert.Equal(Today, saved.CoversFrom);
        Assert.Equal(Today.AddMonths(1).AddDays(-1), saved.CoversThrough);
        Assert.Equal(actor, saved.RegisteredBy);
        Assert.Equal("TRX-1", saved.Reference);
    }

    [Fact]
    public async Task A_gym_cannot_be_created_with_both_trial_and_payment_or_with_neither()
    {
        using var scope = SuperAdmin();
        var handler = scope.ServiceProvider.GetRequiredService<CreateGymHandler>();
        var payment = new CreateGymFirstPayment(100_000m, PaymentMethod.Cash);

        await Assert.ThrowsAsync<ValidationDomainException>(() =>
            handler.HandleAsync(Request($"{Unique("own")}@alta.local", trialDays: 10, payment: payment), default));
        await Assert.ThrowsAsync<ValidationDomainException>(() =>
            handler.HandleAsync(Request($"{Unique("own")}@alta.local"), default));

        var validator = new CreateGymRequestValidator();
        Assert.False(validator.Validate(Request("a@b.co")).IsValid);
        Assert.False(validator.Validate(Request("a@b.co", trialDays: 0)).IsValid);
        Assert.False(validator.Validate(Request("a@b.co", payment: payment with { Amount = 0 })).IsValid);
        Assert.True(validator.Validate(Request("a@b.co", trialDays: 15)).IsValid);
        Assert.True(validator.Validate(Request("a@b.co", payment: payment)).IsValid);
    }

    [Fact]
    public async Task A_first_payment_that_ends_before_today_is_rejected_and_nothing_is_created()
    {
        using var scope = SuperAdmin();
        var request = Request(
            $"{Unique("own")}@alta.local",
            payment: new CreateGymFirstPayment(
                100_000m, PaymentMethod.Cash, Today.AddDays(-40), Today.AddDays(-10)));

        await Assert.ThrowsAsync<ValidationDomainException>(() =>
            scope.ServiceProvider.GetRequiredService<CreateGymHandler>().HandleAsync(request, default));

        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        Assert.False(await db.Set<Gym>().IgnoreQueryFilters().AnyAsync(g => g.Slug == request.Slug.ToLowerInvariant()));
    }

    [Fact]
    public async Task An_existing_account_gets_the_owner_access_and_is_sent_to_log_in_instead_of_a_new_password()
    {
        var email = $"{Unique("own")}@alta.local";
        using var scope = SuperAdmin();
        var sender = scope.ServiceProvider.GetRequiredService<CapturingEmailSender>();
        var handler = scope.ServiceProvider.GetRequiredService<CreateGymHandler>();

        await handler.HandleAsync(Request(email, trialDays: 30), default);
        var second = await handler.HandleAsync(Request(email, trialDays: 30), default);

        var link = EmailLinks.Parse(sender.LastTo(email)!);
        Assert.Equal("/activar", link.Path);
        Assert.Null(link.Code);

        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        Assert.Equal(2, await db.GymAccesses.CountAsync(a => a.UserId == second.OwnerId && a.Role == UserRole.Owner));
    }
}
