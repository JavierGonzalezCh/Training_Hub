using MudBlazor;

namespace GSM.Panel.UI.Navigation.Modules;

/// <summary>Lo que Classes aporta al menú del panel.</summary>
public sealed class ClassesPanelModule : IPanelModule
{
    public string Module => "Classes";

    public IEnumerable<PanelMenuEntry> MenuEntries =>
    [
        new("Clases", Icons.Material.Outlined.CalendarMonth, "/classes", PanelSections.Training, 10,
            BottomBar: true, BottomOrder: 40),
    ];

    public IEnumerable<PanelSettingsSection> SettingsSections =>
        [new("Reservas de clases", 45, typeof(Pages.Settings.ClassRulesTab))];
}
