using GSM.Client;
namespace GSM.Panel.UI.Navigation.Modules;

/// <summary>Lo que Identity aporta a la configuración del panel (cuentas de staff y kioscos).</summary>
public sealed class IdentityPanelModule : IPanelModule
{
    public string Module => "Identity";

    public IEnumerable<PanelSettingsSection> SettingsSections =>
        [
            new("Staff", 50, typeof(Pages.Settings.StaffTab)),
            new("Kioscos", 55, typeof(Pages.Settings.KiosksTab), Roles: GsmRoles.Owner),
        ];
}
