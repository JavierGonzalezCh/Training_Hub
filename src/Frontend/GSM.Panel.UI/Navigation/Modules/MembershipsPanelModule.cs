using GSM.Client;
using MudBlazor;

namespace GSM.Panel.UI.Navigation.Modules;

/// <summary>Lo que Memberships aporta al menú del panel.</summary>
public sealed class MembershipsPanelModule : IPanelModule
{
    public string Module => "Memberships";

    public IEnumerable<PanelMenuEntry> MenuEntries =>
    [
        new("Planes", Icons.Material.Outlined.CardMembership, "/plans", PanelSections.Business, 20),
    ];

    public IEnumerable<PanelSettingsSection> SettingsSections =>
        [new("Membresías", 28, typeof(Pages.Settings.MembershipSettingsTab), Roles: GsmRoles.Owner, Group: PanelSettingsGroups.Memberships,
            Description: "Matrícula, días de gracia y desde cuándo una membresía está por vencer.", TracksUnsavedChanges: true)];
}
