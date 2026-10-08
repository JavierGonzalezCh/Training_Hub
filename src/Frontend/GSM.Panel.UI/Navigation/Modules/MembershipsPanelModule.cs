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
}
