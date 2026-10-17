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

        // Sin pago, el día siguiente al fin de la prueba abre la gracia (S1b), no la mora.
        Assert.Equal(
            EffectiveSubscriptionStatus.TrialEnded,
            GymSubscription.DeriveStatus(GymStatus.Trial, Today, Today.AddDays(-40), trialEndsOn: Today.AddDays(-1)));
    }

    [Fact]
    public void Trial_ended_yesterday_without_payment_is_trial_ended_with_days_left()
    {
        var trialEnd = Today.AddDays(-1);
        Assert.Equal(
            EffectiveSubscriptionStatus.TrialEnded,
            GymSubscription.DeriveStatus(GymStatus.Trial, Today, paidThrough: trialEnd, trialEnd));
        Assert.Equal(2, GymSubscription.DaysUntilDisabled(GymStatus.Trial, Today, trialEnd, trialEnd));
    }

    [Fact]
    public void Trial_ended_stays_trial_ended_through_the_last_grace_day_then_disables()
    {
        var trialEnd = Today.AddDays(-3);
        Assert.Equal(
            EffectiveSubscriptionStatus.TrialEnded,
            GymSubscription.DeriveStatus(GymStatus.Trial, Today, trialEnd, trialEnd));
        Assert.Equal(0, GymSubscription.DaysUntilDisabled(GymStatus.Trial, Today, trialEnd, trialEnd));

        var older = Today.AddDays(-4);
        Assert.Equal(
            EffectiveSubscriptionStatus.Disabled,
            GymSubscription.DeriveStatus(GymStatus.Trial, Today, older, older));
        Assert.Null(GymSubscription.DaysUntilDisabled(GymStatus.Trial, Today, older, older));
    }

    [Fact]
    public void Trial_grace_days_are_respected()
    {
        var trialEnd = Today.AddDays(-5);
        Assert.Equal(
            EffectiveSubscriptionStatus.Disabled,
            GymSubscription.DeriveStatus(GymStatus.Trial, Today, trialEnd, trialEnd, trialGraceDays: 3));
        Assert.Equal(
            EffectiveSubscriptionStatus.TrialEnded,
            GymSubscription.DeriveStatus(GymStatus.Trial, Today, trialEnd, trialEnd, trialGraceDays: 7));
        Assert.Equal(
            EffectiveSubscriptionStatus.Disabled,
            GymSubscription.DeriveStatus(GymStatus.Trial, Today, Today.AddDays(-1), Today.AddDays(-1), trialGraceDays: 0));
    }

    [Fact]
    public void A_confirmed_payment_after_the_trial_is_never_trial_ended()
    {
        var trialEnd = Today.AddDays(-1);
        Assert.Equal(
            EffectiveSubscriptionStatus.Active,
            GymSubscription.DeriveStatus(GymStatus.Trial, Today, Today.AddDays(29), trialEnd));

        // Si el periodo pagado ya venció, el gym es un cliente que pagaba: mora, no fin de prueba.
        Assert.Equal(
            EffectiveSubscriptionStatus.Overdue,
            GymSubscription.DeriveStatus(GymStatus.Trial, Today, Today.AddDays(-1), Today.AddDays(-20)));
    }

    [Fact]
    public void Manual_suspension_wins_over_trial_ended()
        => Assert.Equal(
            EffectiveSubscriptionStatus.Suspended,
            GymSubscription.DeriveStatus(GymStatus.Suspended, Today, Today.AddDays(-1), Today.AddDays(-1)));

    [Fact]
    public void Trial_grace_days_are_validated_on_the_entity()
    {
        var sub = GymSubscription.Create(Guid.NewGuid(), 0m, Today, Today.AddDays(30), Today.AddDays(30));
        Assert.Equal(3, sub.TrialGraceDays);
        sub.SetTrialGraceDays(0);
        Assert.Equal(0, sub.TrialGraceDays);
        Assert.ThrowsAny<Exception>(() => sub.SetTrialGraceDays(-1));
        Assert.ThrowsAny<Exception>(() => sub.SetTrialGraceDays(GymSubscription.MaxTrialGraceDays + 1));
    }

    [Fact]
    public void Trial_without_an_end_date_does_not_shield_an_expired_subscription()
    {
        // Todo gym nace con `gym_status = trial`. Si el nulo valiera como "prueba indefinida",
        // ningún cliente llegaría jamás a overdue ni a disabled y el bloqueo no serviría de nada.
        Assert.Equal(
            EffectiveSubscriptionStatus.Overdue,
            GymSubscription.DeriveStatus(GymStatus.Trial, Today, Today.AddDays(-1), trialEndsOn: null));

        Assert.Equal(
            EffectiveSubscriptionStatus.Disabled,
            GymSubscription.DeriveStatus(GymStatus.Trial, Today, Today.AddDays(-16), trialEndsOn: null));

        Assert.Equal(
            EffectiveSubscriptionStatus.Active,
            GymSubscription.DeriveStatus(GymStatus.Trial, Today, Today.AddDays(5), trialEndsOn: null));
    }

    [Fact]
    public void Days_until_disabled_is_only_set_while_overdue()
    {
        Assert.Equal(9, GymSubscription.DaysUntilDisabled(GymStatus.Active, Today, Today.AddDays(-6), null));
        Assert.Null(GymSubscription.DaysUntilDisabled(GymStatus.Active, Today, Today.AddDays(5), null));
        Assert.Null(GymSubscription.DaysUntilDisabled(GymStatus.Active, Today, Today.AddDays(-30), null));
    }
}
