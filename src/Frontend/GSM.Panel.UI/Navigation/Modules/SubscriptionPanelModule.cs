using GSM.Client;

namespace GSM.Panel.UI.Navigation.Modules;

/// <summary>Lo que SuperAdmin aporta al panel del gym: la suscripción del propio gym, solo lectura para el Owner (S3-3).</summary>
public sealed class SubscriptionPanelModule : IPanelModule
{
    public string Module => "SuperAdmin";

    public IEnumerable<PanelSettingsSection> SettingsSections =>
    [
        new("Suscripción", 27, typeof(Pages.Settings.SubscriptionTab), Roles: GsmRoles.Owner, Group: PanelSettingsGroups.Gym,
            Description: "Tu plan de TrainingHub, hasta cuándo está pagado y los últimos pagos."),
    ];
}
