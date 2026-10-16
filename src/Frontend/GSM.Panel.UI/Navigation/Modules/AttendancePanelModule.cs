using MudBlazor;

namespace GSM.Panel.UI.Navigation.Modules;

/// <summary>Lo que Attendance aporta al menú y a la configuración del panel.</summary>
public sealed class AttendancePanelModule : IPanelModule
{
    public string Module => "Attendance";

    public IEnumerable<PanelMenuEntry> MenuEntries =>
    [
        new("Recepción", Icons.Material.Outlined.QrCodeScanner, "/attendance/checkin", PanelSections.Operation, 20,
            BottomBar: true, BottomOrder: 20),
        new("Asistencia", Icons.Material.Outlined.FactCheck, "/attendance", PanelSections.Operation, 40, MatchAll: true),
    ];

    public IEnumerable<PanelSettingsSection> SettingsSections =>
        [new("Check-in", 30, typeof(Pages.Settings.CheckinTab), Group: PanelSettingsGroups.Entry)];
}
