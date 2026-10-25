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
        // C2: el entrenador (permiso Trainer) ve sus clases; el Owner también, porque los cumple todos.
        new("Mis clases", Icons.Material.Outlined.Sports, "/classes/mine", PanelSections.Training, 11,
            Permission: GSM.Client.GsmPermissions.Trainer),
    ];

    public IEnumerable<PanelSettingsSection> SettingsSections =>
        [new("Reservas de clases", 45, typeof(Pages.Settings.ClassRulesTab), Group: PanelSettingsGroups.Training,
            Description: "Con cuánto se reserva y se cancela, cupos en espera y qué pasa si no llegan.",
            TracksUnsavedChanges: true)];
}
