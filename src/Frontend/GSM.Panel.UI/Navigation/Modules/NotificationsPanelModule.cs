using GSM.Client;

namespace GSM.Panel.UI.Navigation.Modules;

/// <summary>Lo que Notifications aporta a la configuración del panel: el horario sin avisos (solo Owner).</summary>
public sealed class NotificationsPanelModule : IPanelModule
{
    public string Module => "Notifications";

    public IEnumerable<PanelSettingsSection> SettingsSections =>
        [new("Avisos", 45, typeof(Pages.Settings.NotificationsTab), Roles: GsmRoles.Owner, Group: PanelSettingsGroups.Communications)];
}
