namespace GSM.Panel.UI.Navigation.Modules;

/// <summary>Lo que Achievements aporta a la configuración del panel.</summary>
public sealed class AchievementsPanelModule : IPanelModule
{
    public string Module => "Achievements";

    public IEnumerable<PanelSettingsSection> SettingsSections =>
        [new("Logros y premios", 60, typeof(Pages.Settings.AchievementsTab), Group: PanelSettingsGroups.Training,
            Description: "Los logros que ganan los miembros y los premios que se entregan en recepción.")];
}
