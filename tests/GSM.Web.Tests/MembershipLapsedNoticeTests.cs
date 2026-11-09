using Bunit;
using GSM.Client.Generated;
using GSM.Member.UI;
using GSM.Member.UI.Auth;
using GSM.UI.Shared.Auth;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Xunit;

namespace GSM.Web.Tests;

/// <summary>C5 — el aviso de renovación de la app del miembro sin registro en la puerta: informativo, nunca bloquea.</summary>
public class MembershipLapsedNoticeTests : TestContext
{
    private IRenderedComponent<MembershipLapsedNotice> Render(string commercialStatus, string? planName)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var gym = new GymContext();
        gym.Set(new MeResponse { Gym = new MeGym { Name = "Forja" } }, false);
        Services.AddSingleton(gym);
        var member = new MemberContext();
        member.Set(new MemberDetail
        {
            Id = Guid.NewGuid(), FirstName = "Ana", LastName = "Prueba", Status = "Active",
            CommercialStatus = commercialStatus,
            CurrentMembership = planName is null ? null : new MemberCurrentMembership { PlanName = planName },
        });
        Services.AddSingleton(member);
        return RenderComponent<MembershipLapsedNotice>();
    }

    [Fact]
    public void An_expired_plan_is_announced_with_the_gym_name()
    {
        var cut = Render("expired", "Mensual");

        Assert.Contains("Tu plan Mensual está vencido. Comunícate con Forja para renovarlo.",
            cut.Find("[data-testid='membership-lapsed-notice']").TextContent);
    }

    [Fact]
    public void No_plan_gets_its_own_message()
    {
        var cut = Render("none", null);

        Assert.Contains("No tienes un plan vigente. Comunícate con Forja.",
            cut.Find("[data-testid='membership-lapsed-notice']").TextContent);
    }

    [Theory]
    [InlineData("current")]
    [InlineData("expiring")]
    [InlineData("grace")]
    public void A_valid_plan_shows_nothing(string status)
        => Assert.Empty(Render(status, "Mensual").FindAll("[data-testid='membership-lapsed-notice']"));
}
