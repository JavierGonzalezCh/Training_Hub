using MudBlazor;

namespace GSM.Panel.UI.Navigation.Modules;

/// <summary>Lo que Routines aporta al menú del panel.</summary>
public sealed class RoutinesPanelModule : IPanelModule
{
    public string Module => "Routines";

    public IEnumerable<PanelMenuEntry> MenuEntries =>
    [
        new("Ejercicios", Icons.Material.Outlined.FitnessCenter, "/exercises", PanelSections.Training, 20),
        // R8: rutinas cuya fecha de revisión venció o vence esta semana.
        new("Rutinas por revisar", Icons.Material.Outlined.EventRepeat, "/routines/review", PanelSections.Training, 21),
    ];

    public IEnumerable<PanelSettingsSection> SettingsSections =>
        [new("Rutinas", 46, typeof(Pages.Settings.RoutineSettingsTab))];
}
