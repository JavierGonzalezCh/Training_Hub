using GSM.Client;
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

    public IEnumerable<PanelSettingsSection> SettingsSections =>
        [new("Máquinas", 47, typeof(Pages.Settings.EquipmentSettingsTab), Roles: GsmRoles.Owner, Group: PanelSettingsGroups.Training,
            Description: "Quién puede reportar fallas de las máquinas.", TracksUnsavedChanges: true)];
}
