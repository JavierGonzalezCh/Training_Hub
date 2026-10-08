using MudBlazor;

namespace GSM.Panel.UI.Navigation.Modules;

/// <summary>Lo que Communications aporta al menú y a la configuración del panel.</summary>
public sealed class CommunicationsPanelModule : IPanelModule
{
    public string Module => "Communications";

    public IEnumerable<PanelMenuEntry> MenuEntries =>
    [
        new("Comunicaciones", Icons.Material.Outlined.Campaign, "/communications", PanelSections.Business, 40),
    ];

    public IEnumerable<PanelSettingsSection> SettingsSections =>
        [new("Comunicaciones", 40, typeof(Pages.Settings.CommunicationsTab))];
}
