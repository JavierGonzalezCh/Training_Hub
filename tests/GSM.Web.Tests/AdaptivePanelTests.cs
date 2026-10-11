using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using GSM.Client.Generated;
using GSM.Panel.UI.Layout;
using GSM.Panel.UI.Navigation;
using GSM.UI.Shared;
using GSM.UI.Shared.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>AP2: menú armado desde el registro, barra inferior del celular, ServerTable en tarjetas y diálogos a pantalla completa.</summary>
public class AdaptivePanelTests : TestContext
{
    private sealed class TestTab : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddMarkupContent(0, "<p id=\"test-tab\">contenido de prueba</p>");
    }

    private sealed class ExtraModule : IPanelModule
    {
        public string Module => "Extra";

        public IEnumerable<PanelMenuEntry> MenuEntries =>
            [new("Máquinas", Icons.Material.Outlined.FitnessCenter, "/equipment", PanelSections.Operation, 50)];

        public IEnumerable<PanelSettingsSection> SettingsSections =>
            [new("Máquinas y fallas", 70, typeof(TestTab))];
    }

    private void Setup(string role = "owner", bool narrow = false, bool withExtraModule = false)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddGsmUiShared();
        Services.AddGsmPanel();
        if (withExtraModule)
        {
            Services.AddSingleton<IPanelModule, ExtraModule>();
        }

        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("ana");
        auth.SetRoles(role);
        Services.GetRequiredService<ViewportState>().Pin(narrow);
    }

    private static string[] Links(IRenderedComponent<NavMenu> cut)
        => cut.FindAll("a.gsm-nav-link").Select(a => $"{a.TextContent.Trim()}|{a.GetAttribute("href")}").ToArray();

    [Fact]
    public void Desktop_menu_keeps_the_same_entries_routes_and_order_as_before()
    {
        Setup();

        var cut = RenderComponent<NavMenu>();

        Assert.Equal(["Operación", "Negocio", "Entrenamiento", "Gestión"], cut.FindAll(".gsm-nav__label").Select(l => l.TextContent.Trim()));
        Assert.Equal(
            [
                "Dashboard|/", "Recepción|/attendance/checkin", "Kiosco|/kiosk", "Asistencia|/attendance",
                "Miembros|/members", "Planes|/plans", "Pagos y caja|/payments", "Comunicaciones|/communications",
                "Clases|/classes", "Ejercicios|/exercises", "Equipos|/equipment",
                "Configuración|/settings",
            ],
            Links(cut));
    }

    [Fact]
    public void Settings_entry_is_hidden_from_staff()
    {
        Setup(role: "staff");

        var cut = RenderComponent<NavMenu>();

        Assert.DoesNotContain("Configuración", Links(cut).Select(l => l.Split('|')[0]));
        Assert.DoesNotContain("Gestión", cut.FindAll(".gsm-nav__label").Select(l => l.TextContent.Trim()));
    }

    [Fact]
    public void A_module_can_add_a_menu_entry_without_touching_NavMenu()
    {
        Setup(withExtraModule: true);

        var cut = RenderComponent<NavMenu>();

        Assert.Contains("Máquinas|/equipment", Links(cut));
    }

    [Fact]
    public void A_module_can_add_a_settings_tab_without_touching_Settings_Index()
    {
        Setup(withExtraModule: true);
        var registry = Services.GetRequiredService<PanelRegistry>();
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "owner")], "test"));

        var titles = registry.GetSettingsSections(user).Select(s => s.Title).ToArray();

        Assert.Equal(["Datos del gym", "Módulos", "Horario", "Check-in", "Autorizaciones", "Comunicaciones", "Staff", "Logros del gym", "Máquinas y fallas"], titles);
    }

    [Fact]
    public void Settings_index_renders_the_tabs_the_modules_declare()
    {
        // Solo la pestaña de prueba hace falta para comprobar el armado; las reales piden la API.
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(new PanelRegistry([new ExtraModule()]));
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("ana");
        auth.SetRoles("owner");
        RenderComponent<MudPopoverProvider>();

        var cut = RenderComponent<GSM.Panel.UI.Pages.Settings.Index>();

        Assert.Contains("Máquinas y fallas", cut.Markup);
    }

    [Fact]
    public void On_a_phone_the_bottom_bar_shows_the_five_entries_and_More_lists_the_rest()
    {
        Setup(narrow: true);

        var cut = RenderComponent<BottomNav>();

        var labels = cut.FindAll("[data-testid='bottom-nav'] .gsm-tabbar-label").Select(l => l.TextContent.Trim()).ToArray();
        Assert.Equal(["Inicio", "Recepción", "Miembros", "Clases", "Más"], labels);
        Assert.Empty(cut.FindAll("[data-testid='more-sheet']"));

        cut.Find("button.gsm-tabbar__more").Click();

        var more = cut.FindAll("[data-testid='more-sheet'] a.gsm-nav-link").Select(a => a.TextContent.Trim()).ToArray();
        Assert.Equal(["Kiosco", "Asistencia", "Planes", "Pagos y caja", "Comunicaciones", "Ejercicios", "Equipos", "Configuración"], more);
    }

    [Fact]
    public void On_desktop_there_is_no_bottom_bar()
    {
        Setup(narrow: false);

        var cut = RenderComponent<BottomNav>();

        Assert.Empty(cut.FindAll("[data-testid='bottom-nav']"));
    }

    private IRenderedComponent<ServerTable<string>> RenderTable(bool withCard)
    {
        return RenderComponent<ServerTable<string>>(p =>
        {
            p.Add(x => x.Load, (page, size, ct) => Task.FromResult(new ServerTablePage<string>(["Ana", "Luis"], 2)));
            p.Add(x => x.HeaderContent, (RenderFragment)(b => b.AddMarkupContent(0, "<th>Nombre</th>")));
            p.Add(x => x.RowTemplate, item => b => b.AddMarkupContent(0, $"<td>{item}</td>"));
            if (withCard)
            {
                p.Add(x => x.CardTemplate, item => b => b.AddMarkupContent(0, $"<strong class=\"card-body\">{item}</strong>"));
            }
        });
    }

    [Fact]
    public void ServerTable_renders_cards_on_a_phone()
    {
        Setup(narrow: true);
        RenderComponent<MudPopoverProvider>();

        var cut = RenderTable(withCard: true);

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid='server-table-card']").Count));
        Assert.Equal(["Ana", "Luis"], cut.FindAll(".card-body").Select(c => c.TextContent));
        Assert.Empty(cut.FindAll("th"));
    }

    [Fact]
    public void ServerTable_renders_the_table_on_desktop()
    {
        Setup(narrow: false);
        RenderComponent<MudPopoverProvider>();

        var cut = RenderTable(withCard: true);

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("td").Count));
        Assert.NotEmpty(cut.FindAll("th"));
        Assert.Empty(cut.FindAll("[data-testid='server-table-card']"));
    }

    [Fact]
    public void ServerTable_without_a_card_template_keeps_the_table_and_stacks_cells_below_960px()
    {
        Setup(narrow: true);
        RenderComponent<MudPopoverProvider>();

        var cut = RenderTable(withCard: false);

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("td").Count));
        Assert.Contains("mud-sm-table", cut.Find(".mud-table").ClassName);
    }

    [Fact]
    public async Task Dialogs_open_full_screen_on_a_phone_and_not_on_desktop()
    {
        Setup(narrow: true);
        var provider = RenderComponent<PanelDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();

        _ = dialogs.ShowMessageBox("Título", "Mensaje");

        provider.WaitForAssertion(() => Assert.NotEmpty(provider.FindAll(".gsm-dialogs--fullscreen .mud-dialog")));

        Services.GetRequiredService<ViewportState>().Set(false);
        provider.WaitForAssertion(() => Assert.Empty(provider.FindAll(".gsm-dialogs--fullscreen")));
        Assert.NotEmpty(provider.FindAll("[data-testid='dialogs-host'] .mud-dialog"));
        await Task.CompletedTask;
    }
}
