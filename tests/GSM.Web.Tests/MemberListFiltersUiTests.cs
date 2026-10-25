using Bunit;
using Bunit.TestDoubles;
using GSM.Client.Generated;
using GSM.UI.Shared;
using GSM.UI.Shared.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using MembersIndex = GSM.Panel.UI.Pages.Members.Index;

namespace GSM.Web.Tests;

/// <summary>
/// La lista de miembros deja a la vista buscar, la situación y activos/inactivos; etiqueta, entrenador y salud van en
/// "Más filtros" (eran seis controles de una vez), con un contador para que se note si algo está filtrando.
/// </summary>
public class MemberListFiltersUiTests : TestContext
{
    private IRenderedComponent<MembersIndex> Render()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddGsmUiShared();
        var (members, membersFake) = CommercialStatusUiTests.Fake<IMembersClient>();
        membersFake.Handlers["MembersGETAsync"] = _ => new PagedResultOfMemberListItem
        {
            Items = new List<MemberListItem>
            {
                new() { Id = Guid.NewGuid(), FirstName = "Ana", LastName = "Prueba", DocumentNumber = "1", DocumentType = "cc", Status = "Active" },
            },
            Page = 1, PageSize = 20, Total = 1,
        };
        Services.AddSingleton(members);
        var (whatsApp, whatsAppFake) = CommercialStatusUiTests.Fake<IWhatsAppClient>();
        whatsAppFake.Handlers["StatesAsync"] = _ => new List<WhatsAppMemberStateResponse>();
        Services.AddSingleton(whatsApp);
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("ana");
        auth.SetRoles("owner");
        Services.GetRequiredService<ViewportState>().Pin(false);
        RenderComponent<MudPopoverProvider>();
        return RenderComponent<MembersIndex>();
    }

    [Fact]
    public void Tag_trainer_and_health_filters_are_folded_under_More_filters()
    {
        var cut = Render();

        cut.WaitForAssertion(() => Assert.Contains("Ana Prueba", cut.Markup));
        var more = cut.Find("#member-more-filters");
        Assert.DoesNotContain("is-open", more.ClassName);
        Assert.NotNull(more.QuerySelector("#trainer-filter, [id='trainer-filter']"));
        Assert.Equal("Más filtros", cut.Find("[data-testid='more-filters']").TextContent.Trim());

        cut.Find("[data-testid='more-filters']").Click();
        Assert.Contains("is-open", cut.Find("#member-more-filters").ClassName);
        Assert.Equal("true", cut.Find("[data-testid='more-filters']").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void An_active_folded_filter_shows_in_the_button_count()
    {
        var cut = Render();
        cut.WaitForAssertion(() => Assert.Contains("Ana Prueba", cut.Markup));

        cut.Find("[data-testid='more-filters']").Click();
        cut.Find("#member-more-filters input[type='checkbox']").Change(true);

        cut.WaitForAssertion(() => Assert.Equal("Más filtros (1)", cut.Find("[data-testid='more-filters']").TextContent.Trim()));
    }
}
