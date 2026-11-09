using GSM.Client;
using MudBlazor;

namespace GSM.Panel.UI.Navigation.Modules;

/// <summary>Lo que Payments aporta al menú del panel.</summary>
public sealed class PaymentsPanelModule : IPanelModule
{
    public string Module => "Payments";

    public IEnumerable<PanelMenuEntry> MenuEntries =>
    [
        new("Pagos y caja", Icons.Material.Outlined.Payments, "/payments", PanelSections.Business, 30),
    ];

    public IEnumerable<PanelSettingsSection> SettingsSections =>
        [new("Pagos", 29, typeof(Pages.Settings.PaymentSettingsTab), Roles: GsmRoles.Owner, Group: PanelSettingsGroups.Memberships,
            Description: "Si llevas el efectivo por turnos de caja.", TracksUnsavedChanges: true)];
}
