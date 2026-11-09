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

    /// <summary>Una sección con un campo y un "Guardar" que avisa a la página, como las pestañas reales.</summary>
    private sealed class EditableTab : ComponentBase
    {
        [CascadingParameter]
        public SettingsSectionState? SectionState { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "input");
            builder.AddAttribute(1, "id", "editable-field");
            builder.CloseElement();
            builder.OpenElement(2, "button");
            builder.AddAttribute(3, "id", "editable-save");
            builder.AddAttribute(4, "onclick", EventCallback.Factory.Create(this, () => SectionState?.MarkSaved()));
            builder.AddContent(5, "Guardar");
            builder.CloseElement();
        }
    }

    private sealed class ExtraModule : IPanelModule
    {
        public string Module => "Extra";

        public IEnumerable<PanelMenuEntry> MenuEntries =>
            [new("Máquinas", Icons.Material.Outlined.FitnessCenter, "/equipment", PanelSections.Operation, 50)];

        public IEnumerable<PanelSettingsSection> SettingsSections =>
        [
            new("Máquinas y fallas", 70, typeof(TestTab), Description: "Prueba de un módulo extra."),
            new("Ajuste editable", 71, typeof(EditableTab), TracksUnsavedChanges: true),
        ];
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

        Assert.Equal(["Operación", "Administrativo", "Entrenamiento", "Gestión"], cut.FindAll(".gsm-nav__label").Select(l => l.TextContent.Trim()));
        Assert.Equal(
            [
                "Dashboard|/", "Recepción|/attendance/checkin", "Asistencia|/attendance",
                "Miembros|/members", "Planes|/plans", "Pagos y caja|/payments", "Comunicaciones|/communications",
                "Clases|/classes", "Ejercicios|/exercises", "Rutinas por revisar|/routines/review", "Plantillas de rutina|/routine-templates", "Cumplimiento|/routines/adherence", "Equipos|/equipment",
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

        Assert.Equal(
            [
                "Datos del gym", "Horario", "Módulos", "Suscripción", "Membresías", "Pagos", "Check-in", "Autorizaciones", "Privacidad",
                "Mensajes a los miembros", "Reservas de clases", "Horario sin avisos", "Rutinas", "Máquinas", "Personal y permisos",
                "Kioscos", "Logros y premios", "Máquinas y fallas", "Ajuste editable",
            ],
            titles);
    }

    [Fact]
    public void Settings_are_grouped_by_the_owners_tasks_not_one_group_per_module()
    {
        Setup(withExtraModule: true);
        var registry = Services.GetRequiredService<PanelRegistry>();
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "owner")], "test"));

        var groups = registry.GetSettingsGroups(user);

        Assert.Equal(
            ["Tu gimnasio", "Personal", "Entrada al gym", "Membresías y miembros", "Entrenamiento", "Comunicaciones", "Otros"],
            groups.Select(g => g.Name).ToArray());
        Assert.Equal(["Datos del gym", "Horario", "Módulos", "Suscripción"], Titles(groups, "Tu gimnasio"));
        Assert.Equal(["Personal y permisos"], Titles(groups, "Personal"));
        Assert.Equal(["Check-in", "Kioscos"], Titles(groups, "Entrada al gym"));
        Assert.Equal(["Membresías", "Pagos", "Autorizaciones", "Privacidad"], Titles(groups, "Membresías y miembros"));
        Assert.Equal(["Reservas de clases", "Rutinas", "Máquinas", "Logros y premios"], Titles(groups, "Entrenamiento"));
        Assert.Equal(["Mensajes a los miembros", "Horario sin avisos"], Titles(groups, "Comunicaciones"));
        // Una sección sin grupo cae en "Otros", al final.
        Assert.Equal(["Máquinas y fallas", "Ajuste editable"], groups.Last().Sections.Select(s => s.Title).ToArray());
        // Las secciones reales dicen qué hay dentro.
        Assert.All(groups.Where(g => g.Name != "Otros").SelectMany(g => g.Sections), s => Assert.False(string.IsNullOrWhiteSpace(s.Description)));
    }

    private static string[] Titles(IReadOnlyList<PanelSettingsGroup> groups, string name)
        => groups.Single(g => g.Name == name).Sections.Select(s => s.Title).ToArray();

    private void SetupSettingsPages()
    {
        // Solo las secciones de prueba: las reales piden la API.
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(new PanelRegistry([new ExtraModule()]));
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("ana");
        auth.SetRoles("owner");
        RenderComponent<MudPopoverProvider>();
    }

    [Fact]
    public void Settings_index_lists_each_section_as_a_link_to_its_own_page()
    {
        SetupSettingsPages();

        var cut = RenderComponent<GSM.Panel.UI.Pages.Settings.Index>();

        Assert.Contains("Otros", cut.Find("[data-testid='settings-groups']").TextContent);
        var link = cut.Find("[data-testid='settings-link-maquinas-y-fallas']");
        Assert.Equal("/settings/maquinas-y-fallas", link.GetAttribute("href"));
        Assert.Contains("Prueba de un módulo extra.", link.TextContent);
        // El índice no monta las secciones: cada una abre su página.
        Assert.DoesNotContain("contenido de prueba", cut.Markup);
    }

    [Fact]
    public void Settings_index_filters_sections_by_name_or_description_ignoring_accents()
    {
        SetupSettingsPages();
        var cut = RenderComponent<GSM.Panel.UI.Pages.Settings.Index>();

        cut.Find("[data-testid='settings-search'] input, input[data-testid='settings-search']").Input("maquinas");
        Assert.Single(cut.FindAll("a.gsm-settings-link"));

        cut.Find("[data-testid='settings-search'] input, input[data-testid='settings-search']").Input("zzz");
        Assert.NotNull(cut.Find("[data-testid='settings-search-empty']"));
    }

    [Fact]
    public void An_old_settings_link_with_group_and_section_redirects_to_the_section_page()
    {
        SetupSettingsPages();
        var nav = Services.GetRequiredService<FakeNavigationManager>();
        nav.NavigateTo("/settings?group=Otros&section=Ajuste%20editable");

        RenderComponent<GSM.Panel.UI.Pages.Settings.Index>();

        Assert.EndsWith("/settings/ajuste-editable", nav.Uri);
    }

    [Fact]
    public void A_section_page_shows_its_component_and_an_unknown_slug_says_so()
    {
        SetupSettingsPages();

        var page = RenderComponent<GSM.Panel.UI.Pages.Settings.Section>(p => p.Add(x => x.Slug, "maquinas-y-fallas"));
        Assert.Contains("contenido de prueba", page.Markup);
        Assert.Contains("Máquinas y fallas", page.Find("h1").TextContent);

        var missing = RenderComponent<GSM.Panel.UI.Pages.Settings.Section>(p => p.Add(x => x.Slug, "no-existe"));
        Assert.NotNull(missing.Find("[data-testid='settings-section-missing']"));
    }

    [Fact]
    public void Editing_a_section_shows_unsaved_changes_and_saving_clears_it()
    {
        SetupSettingsPages();
        var page = RenderComponent<GSM.Panel.UI.Pages.Settings.Section>(p => p.Add(x => x.Slug, "ajuste-editable"));
        Assert.Empty(page.FindAll("[data-testid='settings-unsaved']"));

        page.Find("#editable-field").Input("nuevo valor");
        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='settings-unsaved']")));

        page.Find("#editable-save").Click();
        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-testid='settings-unsaved']")));
    }

    [Fact]
    public void Leaving_a_section_with_unsaved_changes_asks_first_and_can_stay()
    {
        SetupSettingsPages();
        var provider = RenderComponent<MudDialogProvider>();
        var nav = Services.GetRequiredService<FakeNavigationManager>();
        nav.NavigateTo("/settings/ajuste-editable");
        var page = RenderComponent<GSM.Panel.UI.Pages.Settings.Section>(p => p.Add(x => x.Slug, "ajuste-editable"));

        page.Find("#editable-field").Input("nuevo valor");
        page.InvokeAsync(() => nav.NavigateTo("/members"));

        provider.WaitForAssertion(() => Assert.Contains("Cambios sin guardar", provider.Markup));
        provider.FindAll("button").First(b => b.TextContent.Contains("Seguir editando")).Click();
        page.WaitForAssertion(() => Assert.EndsWith("/settings/ajuste-editable", nav.Uri));
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
        Assert.Equal(["Asistencia", "Planes", "Pagos y caja", "Comunicaciones", "Ejercicios", "Rutinas por revisar", "Plantillas de rutina", "Cumplimiento", "Equipos", "Configuración"], more);
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
