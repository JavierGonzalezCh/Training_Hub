using GSM.Client;
using MudBlazor;

namespace GSM.Panel.UI.Navigation.Modules;

/// <summary>Lo que Gyms aporta al menú y a la configuración del panel.</summary>
public sealed class GymsPanelModule : IPanelModule
{
    public string Module => "Gyms";

    public IEnumerable<PanelMenuEntry> MenuEntries =>
    [
        new("Configuración", Icons.Material.Outlined.Settings, "/settings", PanelSections.Management, 10,
            Roles: GsmRoles.Owner),
    ];

    public IEnumerable<PanelSettingsSection> SettingsSections =>
    [
        new("Datos del gym", 10, typeof(Pages.Settings.GymDataTab), Group: PanelSettingsGroups.Gym),
        new("Módulos", 20, typeof(Pages.Settings.ModulesTab), Group: PanelSettingsGroups.Modules),
        new("Horario", 25, typeof(Pages.Settings.OpeningHoursTab), Group: PanelSettingsGroups.Gym),
    ];
}
