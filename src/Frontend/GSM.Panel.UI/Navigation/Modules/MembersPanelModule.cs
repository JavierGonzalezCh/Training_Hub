using GSM.Client;
using MudBlazor;

namespace GSM.Panel.UI.Navigation.Modules;

/// <summary>Lo que Members aporta al menú y a la configuración del panel.</summary>
public sealed class MembersPanelModule : IPanelModule
{
    public string Module => "Members";

    public IEnumerable<PanelMenuEntry> MenuEntries =>
    [
        new("Miembros", Icons.Material.Outlined.Groups, "/members", PanelSections.Business, 10,
            BottomBar: true, BottomOrder: 30),
    ];

    public IEnumerable<PanelSettingsSection> SettingsSections =>
    [
        new("Autorizaciones", 35, typeof(Pages.Settings.ConsentDocumentsTab), GsmRoles.Owner),
        new("Privacidad", 36, typeof(Pages.Settings.AnonymizationTab), GsmRoles.Owner),
    ];
}
