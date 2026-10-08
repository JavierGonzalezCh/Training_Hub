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
}
