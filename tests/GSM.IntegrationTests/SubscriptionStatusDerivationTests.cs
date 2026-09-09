using GSM.Domain.Gyms;
using GSM.Domain.SuperAdmin;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>
/// Derivación del estado efectivo: método puro, sin base de datos. <c>overdue</c> y <c>disabled</c>
/// salen del paso del tiempo; <c>suspended</c> solo del estado manual.
/// </summary>
public sealed class SubscriptionStatusDerivationTests
{
    private static readonly DateOnly Today = new(2026, 9, 9);

    [Fact]
    public void Paid_through_in_the_future_is_active()
        => Assert.Equal(
            EffectiveSubscriptionStatus.Active,
            GymSubscription.DeriveStatus(GymStatus.Active, Today, Today.AddDays(5), null));

    [Fact]
    public void Expired_yesterday_is_overdue_without_running_anything()
        => Assert.Equal(
            EffectiveSubscriptionStatus.Overdue,
            GymSubscription.DeriveStatus(GymStatus.Active, Today, Today.AddDays(-1), null));

    [Fact]
    public void Expired_sixteen_days_ago_is_disabled_by_the_passage_of_time()
        => Assert.Equal(
            EffectiveSubscriptionStatus.Disabled,
            GymSubscription.DeriveStatus(GymStatus.Active, Today, Today.AddDays(-16), null));

    [Fact]
    public void Exactly_fifteen_days_after_expiry_is_still_overdue()
        => Assert.Equal(
            EffectiveSubscriptionStatus.Overdue,
            GymSubscription.DeriveStatus(GymStatus.Active, Today, Today.AddDays(-15), null));

    [Fact]
    public void Manual_suspension_always_wins_even_if_paid_through_is_current()
        => Assert.Equal(
            EffectiveSubscriptionStatus.Suspended,
            GymSubscription.DeriveStatus(GymStatus.Suspended, Today, Today.AddDays(30), null));

    [Fact]
    public void Trial_holds_until_trial_ends_on_then_falls_through_to_payment_logic()
    {
        Assert.Equal(
            EffectiveSubscriptionStatus.Trial,
            GymSubscription.DeriveStatus(GymStatus.Trial, Today, Today.AddDays(-40), trialEndsOn: Today.AddDays(3)));

        Assert.Equal(
            EffectiveSubscriptionStatus.Disabled,
            GymSubscription.DeriveStatus(GymStatus.Trial, Today, Today.AddDays(-40), trialEndsOn: Today.AddDays(-1)));
    }

    [Fact]
    public void Days_until_disabled_is_only_set_while_overdue()
    {
        Assert.Equal(9, GymSubscription.DaysUntilDisabled(GymStatus.Active, Today, Today.AddDays(-6), null));
        Assert.Null(GymSubscription.DaysUntilDisabled(GymStatus.Active, Today, Today.AddDays(5), null));
        Assert.Null(GymSubscription.DaysUntilDisabled(GymStatus.Active, Today, Today.AddDays(-30), null));
    }
}
