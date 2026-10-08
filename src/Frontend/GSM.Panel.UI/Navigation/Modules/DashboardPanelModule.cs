using MudBlazor;

namespace GSM.Panel.UI.Navigation.Modules;

/// <summary>Lo que el dashboard aporta al menú del panel.</summary>
public sealed class DashboardPanelModule : IPanelModule
{
    public string Module => "Dashboard";

    public IEnumerable<PanelMenuEntry> MenuEntries =>
    [
        new("Dashboard", Icons.Material.Outlined.SpaceDashboard, "/", PanelSections.Operation, 10,
            MatchAll: true, BottomBar: true, BottomOrder: 10, BottomTitle: "Inicio"),
    ];
}
