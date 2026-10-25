using GSM.Client;
namespace GSM.Panel.UI.Navigation.Modules;

/// <summary>Lo que Identity aporta a la configuración del panel (cuentas de staff y kioscos).</summary>
public sealed class IdentityPanelModule : IPanelModule
{
    public string Module => "Identity";

    public IEnumerable<PanelSettingsSection> SettingsSections =>
        [
            new("Personal y permisos", 50, typeof(Pages.Settings.StaffTab), Group: PanelSettingsGroups.Team,
                Description: "Dueños, recepción y entrenadores, y lo que puede hacer cada uno."),
            new("Kioscos", 55, typeof(Pages.Settings.KiosksTab), Roles: GsmRoles.Owner, Group: PanelSettingsGroups.Entry,
                Description: "Las tablets de la entrada donde los miembros registran su llegada."),
        ];
}
