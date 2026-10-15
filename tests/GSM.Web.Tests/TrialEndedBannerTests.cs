using Bunit;
using GSM.Client.Generated;
using GSM.UI.Shared.Auth;
using GSM.UI.Shared.Components;
using GSM.UI.Shared.Formatting;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>S1b: el letrero de "prueba terminada" del panel y el estado derivado en el cliente.</summary>
public class TrialEndedBannerTests : TestContext
{
    private GymContext Setup(string status, int? daysUntilDisabled)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var context = new GymContext();
        context.Set(new MeResponse
        {
            Subscription = new SubscriptionInfo
            {
                Status = status,
                PaidThrough = new DateOnly(2026, 10, 14),
                DaysUntilDisabled = daysUntilDisabled,
            },
        }, impersonating: false);
        Services.AddSingleton(context);
        return context;
    }

    [Fact]
    public void Trial_ended_shows_the_confirm_payment_banner_with_days_left()
    {
        var context = Setup("TrialEnded", 2);

        var cut = RenderComponent<SubscriptionBanner>();

        Assert.Equal(SubscriptionState.TrialEnded, context.SubscriptionState);
        Assert.False(context.ReadOnly); // acceso total
        var banner = cut.Find("[data-testid='trial-ended-banner']");
        Assert.Contains("Tu prueba terminó. Confirma tu pago para seguir usando GSM", banner.TextContent);
        Assert.Contains("Te quedan 2 días", banner.TextContent);
    }

    [Fact]
    public void Trial_ended_on_the_last_grace_day_says_so()
    {
        Setup("TrialEnded", 0);

        var cut = RenderComponent<SubscriptionBanner>();

        Assert.Contains("último día", cut.Find("[data-testid='trial-ended-banner']").TextContent);
    }

    [Fact]
    public void Active_gym_shows_no_trial_ended_banner()
    {
        Setup("Active", null);

        var cut = RenderComponent<SubscriptionBanner>();

        Assert.Empty(cut.FindAll("[data-testid='trial-ended-banner']"));
    }

    [Fact]
    public void Disabled_after_the_trial_grace_is_read_only()
    {
        var context = Setup("Disabled", null);

        Assert.True(context.ReadOnly);
        Assert.Equal("Prueba terminada", SuperAdminLabels.StatusLabel("TrialEnded"));
    }
}
