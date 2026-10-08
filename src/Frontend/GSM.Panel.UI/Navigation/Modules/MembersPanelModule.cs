using MudBlazor;

namespace GSM.Panel.UI.Navigation.Modules;

/// <summary>Lo que Members aporta al menú del panel.</summary>
public sealed class MembersPanelModule : IPanelModule
{
    public string Module => "Members";

    public IEnumerable<PanelMenuEntry> MenuEntries =>
    [
        new("Miembros", Icons.Material.Outlined.Groups, "/members", PanelSections.Business, 10,
            BottomBar: true, BottomOrder: 30),
    ];
}
