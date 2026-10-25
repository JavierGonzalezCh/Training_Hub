using GSM.Client;

namespace GSM.Panel.UI.Navigation.Modules;

/// <summary>Lo que Notifications aporta a la configuración del panel: el horario sin avisos (solo Owner).</summary>
public sealed class NotificationsPanelModule : IPanelModule
{
    public string Module => "Notifications";

    public IEnumerable<PanelSettingsSection> SettingsSections =>
        [new("Horario sin avisos", 45, typeof(Pages.Settings.NotificationsTab), Roles: GsmRoles.Owner, Group: PanelSettingsGroups.Communications,
            Description: "Las horas en que no se envían notificaciones a los miembros.", TracksUnsavedChanges: true)];
}
