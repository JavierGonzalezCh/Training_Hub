using MudBlazor;

namespace GSM.Panel.UI.Navigation.Modules;

/// <summary>Lo que Equipment aporta al menú del panel.</summary>
public sealed class EquipmentPanelModule : IPanelModule
{
    public string Module => "Equipment";

    public IEnumerable<PanelMenuEntry> MenuEntries =>
    [
        new("Equipos", Icons.Material.Outlined.Inventory2, "/equipment", PanelSections.Training, 30),
    ];
}
