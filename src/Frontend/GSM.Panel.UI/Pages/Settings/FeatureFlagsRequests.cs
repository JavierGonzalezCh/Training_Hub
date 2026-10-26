using GSM.Client.Generated;

namespace GSM.Panel.UI.Pages.Settings;

/// <summary>
/// El PUT de flags exige todos los campos. Las secciones de Rutinas y Equipos editan cada una su parte:
/// parten de lo que el servidor tiene hoy y cambian solo lo suyo.
/// </summary>
internal static class FeatureFlagsRequests
{
    public static UpdateFeatureFlagsRequest From(GymFeatureFlagsResponse flags) => new()
    {
        ShowRoutines = flags.ShowRoutines,
        ShowClasses = flags.ShowClasses,
        ShowProgress = flags.ShowProgress,
        ShowGamification = flags.ShowGamification,
        ShowCommunity = flags.ShowCommunity,
        CheckinQr = flags.CheckinQr,
        MembersCanCreateRoutines = flags.MembersCanCreateRoutines,
        MembersCanReportEquipment = flags.MembersCanReportEquipment,
        ShowRanking = flags.ShowRanking,
        ShowFrontDesk = flags.ShowFrontDesk,
    };
}
