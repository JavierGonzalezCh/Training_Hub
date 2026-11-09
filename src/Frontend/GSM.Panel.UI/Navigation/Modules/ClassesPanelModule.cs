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
        // «Mis clases» ya no es otra entrada del menú: quien tiene el permiso Trainer (y el Owner) lo ve como una pestaña dentro
        // de Clases. La ruta /classes/mine se conserva para los enlaces de las notificaciones.
    ];

    public IEnumerable<PanelSettingsSection> SettingsSections =>
        [new("Reservas de clases", 45, typeof(Pages.Settings.ClassRulesTab), Group: PanelSettingsGroups.Training,
            Description: "Con cuánto se reserva y se cancela, cupos en espera y qué pasa si no llegan.",
            TracksUnsavedChanges: true)];
}
