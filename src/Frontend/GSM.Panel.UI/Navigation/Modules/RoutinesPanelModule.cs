using MudBlazor;

namespace GSM.Panel.UI.Navigation.Modules;

/// <summary>Lo que Routines aporta al menú del panel.</summary>
public sealed class RoutinesPanelModule : IPanelModule
{
    public string Module => "Routines";

    public IEnumerable<PanelMenuEntry> MenuEntries =>
    [
        new("Ejercicios", Icons.Material.Outlined.FitnessCenter, "/exercises", PanelSections.Training, 20),
    ];
}
