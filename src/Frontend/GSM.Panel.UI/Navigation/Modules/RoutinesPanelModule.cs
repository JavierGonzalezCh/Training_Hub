using GSM.Client;
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
        // R11: plantillas reutilizables del gym.
        new("Plantillas de rutina", Icons.Material.Outlined.ContentCopy, "/routine-templates", PanelSections.Training, 22),
    ];

    public IEnumerable<PanelSettingsSection> SettingsSections =>
        [new("Rutinas", 46, typeof(Pages.Settings.RoutineSettingsTab), Roles: GsmRoles.Owner, Group: PanelSettingsGroups.Training,
            Description: "Rutinas en la app del miembro y cada cuánto se revisan.", TracksUnsavedChanges: true)];
}
