using GSM.Client;
using MudBlazor;

namespace GSM.Panel.UI.Navigation.Modules;

/// <summary>Lo que Members aporta al menú y a la configuración del panel.</summary>
public sealed class MembersPanelModule : IPanelModule
{
    public string Module => "Members";

    public IEnumerable<PanelMenuEntry> MenuEntries =>
    [
        new("Miembros", Icons.Material.Outlined.Groups, "/members", PanelSections.Business, 10,
            BottomBar: true, BottomOrder: 30),
    ];

    public IEnumerable<PanelSettingsSection> SettingsSections =>
    [
        new("Autorizaciones", 35, typeof(Pages.Settings.ConsentDocumentsTab), GsmRoles.Owner, PanelSettingsGroups.Memberships,
            Description: "Los textos que aceptan los miembros: tratamiento de datos y exoneración de responsabilidad."),
        new("Privacidad", 36, typeof(Pages.Settings.AnonymizationTab), GsmRoles.Owner, PanelSettingsGroups.Memberships,
            Description: "Cuánto tiempo después de la baja se borran los datos personales de un miembro.", TracksUnsavedChanges: true),
    ];
}
