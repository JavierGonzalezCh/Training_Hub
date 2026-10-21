using Bunit;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Settings;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>S3-3: la pestaña de suscripción del Owner es de solo lectura y no ofrece pagar desde el sistema.</summary>
public class OwnSubscriptionUiTests : TestContext
{
    private IRenderedComponent<SubscriptionTab> Render(OwnSubscriptionResponse response)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var (gym, fake) = CommercialStatusUiTests.Fake<IGymClient>();
        fake.Handlers["OwnSubscriptionAsync"] = _ => response;
        Services.AddSingleton(gym);
        RenderComponent<MudBlazor.MudPopoverProvider>();
        return RenderComponent<SubscriptionTab>();
    }

    private static OwnSubscriptionResponse Sample(string status) => new()
    {
        Plan = "Pro",
        MonthlyPrice = 200000,
        EffectiveStatus = status,
        PaidThrough = new DateOnly(2026, 11, 30),
        MemberLimit = 500,
        CountedMembers = 142,
        RecentPayments =
        [
            new OwnSubscriptionPayment
            {
                Id = Guid.NewGuid(), Amount = 200000, Method = "BankTransfer",
                CoversFrom = new DateOnly(2026, 10, 1), CoversThrough = new DateOnly(2026, 10, 31),
                PaidAt = new DateTimeOffset(2026, 10, 1, 15, 0, 0, TimeSpan.Zero),
            },
        ],
    };

    [Fact]
    public void It_shows_plan_status_usage_and_payments_without_any_pay_button()
    {
        var cut = Render(Sample("Active"));

        Assert.Equal("Pro", cut.Find("[data-testid='subscription-plan']").TextContent.Trim());
        Assert.Contains("Al día", cut.Find("[data-testid='subscription-status']").TextContent);
        Assert.Contains("142 / 500", cut.Find("[data-testid='subscription-members']").TextContent);
        Assert.NotEmpty(cut.FindAll("[data-testid='subscription-paid-through']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='subscription-payments']"));
        Assert.Contains("comunícate con TrainingHub", cut.Find("[data-testid='subscription-renew-note']").TextContent);
        Assert.Empty(cut.FindAll("button"));
    }

    [Fact]
    public void A_suspended_gym_shows_the_reason()
    {
        var response = Sample("Suspended");
        response.SuspensionReason = "Cobro pendiente";

        var cut = Render(response);

        Assert.Contains("Suspendido", cut.Find("[data-testid='subscription-status']").TextContent);
        Assert.Contains("Cobro pendiente", cut.Find("[data-testid='subscription-reason']").TextContent);
    }

    [Fact]
    public void A_trial_shows_when_it_ends_instead_of_the_paid_through_date()
    {
        var response = Sample("Trial");
        response.TrialEndsOn = new DateOnly(2026, 11, 5);

        var cut = Render(response);

        Assert.Contains("En prueba", cut.Find("[data-testid='subscription-status']").TextContent);
        Assert.NotEmpty(cut.FindAll("[data-testid='subscription-trial-end']"));
        Assert.Empty(cut.FindAll("[data-testid='subscription-paid-through']"));
    }

    [Fact]
    public void Overdue_and_disabled_use_the_owner_wording()
    {
        Assert.Contains("En mora", Render(Sample("Overdue")).Find("[data-testid='subscription-status']").TextContent);
    }
}
