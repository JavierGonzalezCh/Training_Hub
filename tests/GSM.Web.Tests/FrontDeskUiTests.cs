using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using GSM.Client.Generated;
using GSM.Panel.UI.Layout;
using GSM.Panel.UI.Navigation;
using GSM.UI.Shared;
using GSM.UI.Shared.Auth;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>T5 — con la opción «Recepción, kiosco y aforo» apagada, el panel oculta Recepción; no bloquea nada más.</summary>
public class FrontDeskUiTests : TestContext
{
    private static ClaimsPrincipal Owner()
        => new(new ClaimsIdentity([new Claim(ClaimTypes.Role, "owner"), new Claim(ClaimTypes.Name, "ana")], "test"));

    private static GymContext Gym(bool? showFrontDesk)
    {
        var gym = new GymContext();
        gym.Set(
            new MeResponse
            {
                Role = "owner",
                FeatureFlags = showFrontDesk is null ? null : new MeFeatureFlags { ShowFrontDesk = showFrontDesk },
            },
            false);
        return gym;
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(null, true)]   // sin flags cargados se muestra
    [InlineData(false, false)]
    public void Reception_is_in_the_menu_and_the_bottom_bar_only_when_front_desk_is_on(bool? flag, bool visible)
    {
        Services.AddGsmPanel();
        var registry = Services.GetRequiredService<PanelRegistry>();
        var gym = Gym(flag);

        var menu = registry.GetMenu(Owner(), gym).SelectMany(g => g.Entries).Select(e => e.Title);
        var bottom = registry.GetBottomBar(Owner(), gym).Select(e => e.Title);

        Assert.Equal(visible, menu.Contains("Recepción"));
        Assert.Equal(visible, bottom.Contains("Recepción"));
        // Nada más desaparece: la asistencia y el resto del menú siguen ahí.
        Assert.Contains("Asistencia", menu);
        Assert.Contains("Miembros", menu);
    }

    [Fact]
    public void Without_a_gym_context_the_registry_keeps_every_entry()
    {
        Services.AddGsmPanel();
        var registry = Services.GetRequiredService<PanelRegistry>();

        Assert.Contains("Recepción", registry.GetMenu(Owner()).SelectMany(g => g.Entries).Select(e => e.Title));
    }

    [Fact]
    public void The_rendered_menu_drops_reception_when_the_option_is_off()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddGsmUiShared();
        Services.AddGsmPanel();
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("ana");
        auth.SetRoles("owner");
        Services.GetRequiredService<GymContext>().Set(
            new MeResponse { Role = "owner", FeatureFlags = new MeFeatureFlags { ShowFrontDesk = false } }, false);

        var cut = RenderComponent<NavMenu>();

        var links = cut.FindAll("a.gsm-nav-link").Select(a => a.TextContent.Trim()).ToArray();
        Assert.DoesNotContain("Recepción", links);
        Assert.Contains("Asistencia", links);
    }
}
