using GSM.Client.Generated;
using GSM.UI.Shared.Formatting;

namespace GSM.Panel.UI.Pages.Settings;

/// <summary>Una opción en la que la configuración actual del gym difiere de la recomendada para su tipo.</summary>
/// <param name="Key">Identificador estable, <c>grupo.opción</c>.</param>
/// <param name="Group">Módulo que la guarda: <c>modules</c>, <c>checkin</c>, <c>memberships</c> o <c>routines</c>.</param>
public sealed record ConfigChoice(string Key, string Group, string Label, string Current, string Recommended);

/// <summary>
/// Compara la configuración actual del gym con la que recomienda la plantilla de su tipo (docs/GSM-tipos-de-negocio.md §9) y
/// arma, para lo que el dueño marque, las peticiones normales de cada módulo. No hay endpoint «aplicar plantilla»: cada
/// cambio se guarda por el endpoint de su módulo y parte de lo que el servidor tiene hoy, así que solo cambia lo marcado.
/// </summary>
public sealed class RecommendedConfigDiff(
    GymFeatureFlagsResponse flags,
    CheckinSettingsResponse checkin,
    MembershipSettingsResponse memberships,
    RoutineSettingsResponse routines,
    RecommendedConfiguration recommended)
{
    public IReadOnlyList<ConfigChoice> Differences()
    {
        var rec = recommended.Modules;
        var list = new List<ConfigChoice>();

        void Flag(string key, string label, bool current, bool wanted)
        {
            if (current != wanted)
            {
                list.Add(new ConfigChoice($"modules.{key}", "modules", label, OnOff(current), OnOff(wanted)));
            }
        }

        Flag(nameof(flags.ShowRoutines), "Rutinas", flags.ShowRoutines, rec.ShowRoutines);
        Flag(nameof(flags.ShowClasses), "Clases", flags.ShowClasses, rec.ShowClasses);
        Flag(nameof(flags.ShowProgress), "Progreso y racha", flags.ShowProgress, rec.ShowProgress);
        Flag(nameof(flags.ShowGamification), "Logros", flags.ShowGamification, rec.ShowGamification);
        Flag(nameof(flags.ShowCommunity), "Comunidad", flags.ShowCommunity, rec.ShowCommunity);
        Flag(nameof(flags.CheckinQr), "Check-in con QR", flags.CheckinQr, rec.CheckinQr);
        Flag(nameof(flags.MembersCanReportEquipment), "Reporte de fallas de máquinas", flags.MembersCanReportEquipment, rec.MembersCanReportEquipment);
        Flag(nameof(flags.ShowRanking), "Ranking de constancia", flags.ShowRanking, rec.ShowRanking);
        Flag(nameof(flags.ShowFrontDesk), "Recepción, kiosco y aforo", flags.ShowFrontDesk != false, rec.ShowFrontDesk != false);

        var wantedCheckin = recommended.Checkin;
        if (!string.Equals(checkin.Mode, wantedCheckin.Mode, StringComparison.OrdinalIgnoreCase))
        {
            list.Add(new ConfigChoice("checkin.Mode", "checkin", "Tipo de QR en la entrada", ModeText(checkin.Mode), ModeText(wantedCheckin.Mode)));
        }

        if (checkin.AssumedStayMinutes != wantedCheckin.AssumedStayMinutes)
        {
            list.Add(new ConfigChoice(
                "checkin.AssumedStayMinutes", "checkin", "Permanencia asumida",
                $"{checkin.AssumedStayMinutes} min", $"{wantedCheckin.AssumedStayMinutes} min"));
        }

        if ((checkin.RemoteTraining == true) != wantedCheckin.RemoteTraining)
        {
            list.Add(new ConfigChoice(
                "checkin.RemoteTraining", "checkin", "Mis miembros entrenan fuera de mi sede",
                OnOff(checkin.RemoteTraining == true), OnOff(wantedCheckin.RemoteTraining)));
        }

        var wantedMemberships = recommended.Memberships;
        if (memberships.GraceDays != wantedMemberships.GraceDays)
        {
            list.Add(new ConfigChoice(
                "memberships.GraceDays", "memberships", "Días de gracia",
                memberships.GraceDays.ToString(), wantedMemberships.GraceDays.ToString()));
        }

        if (memberships.ExpiringDays != wantedMemberships.ExpiringDays)
        {
            list.Add(new ConfigChoice(
                "memberships.ExpiringDays", "memberships", "Aviso de «por vencer» (días)",
                memberships.ExpiringDays.ToString(), wantedMemberships.ExpiringDays.ToString()));
        }

        if (memberships.EnrollmentFee != wantedMemberships.EnrollmentFee)
        {
            list.Add(new ConfigChoice(
                "memberships.EnrollmentFee", "memberships", "Matrícula",
                GsmFormat.Money((decimal)memberships.EnrollmentFee), GsmFormat.Money((decimal)wantedMemberships.EnrollmentFee)));
        }

        if (routines.ReviewWeeks != recommended.Routines.ReviewWeeks)
        {
            list.Add(new ConfigChoice(
                "routines.ReviewWeeks", "routines", "Semanas entre revisiones de rutina",
                routines.ReviewWeeks.ToString(), recommended.Routines.ReviewWeeks.ToString()));
        }

        return list;
    }

    /// <summary>El PUT de módulos con lo marcado cambiado y todo lo demás como está hoy; nulo si no se marcó nada de módulos.</summary>
    public UpdateFeatureFlagsRequest? FlagsRequest(ISet<string> selected)
    {
        if (!selected.Any(k => k.StartsWith("modules.", StringComparison.Ordinal)))
        {
            return null;
        }

        bool Pick(string name, bool current, bool wanted) => selected.Contains($"modules.{name}") ? wanted : current;

        var rec = recommended.Modules;
        var request = FeatureFlagsRequests.From(flags);
        request.ShowRoutines = Pick(nameof(flags.ShowRoutines), flags.ShowRoutines, rec.ShowRoutines);
        request.ShowClasses = Pick(nameof(flags.ShowClasses), flags.ShowClasses, rec.ShowClasses);
        request.ShowProgress = Pick(nameof(flags.ShowProgress), flags.ShowProgress, rec.ShowProgress);
        request.ShowGamification = Pick(nameof(flags.ShowGamification), flags.ShowGamification, rec.ShowGamification);
        request.ShowCommunity = Pick(nameof(flags.ShowCommunity), flags.ShowCommunity, rec.ShowCommunity);
        request.CheckinQr = Pick(nameof(flags.CheckinQr), flags.CheckinQr, rec.CheckinQr);
        request.MembersCanReportEquipment = Pick(
            nameof(flags.MembersCanReportEquipment), flags.MembersCanReportEquipment, rec.MembersCanReportEquipment);
        request.ShowRanking = Pick(nameof(flags.ShowRanking), flags.ShowRanking, rec.ShowRanking);
        request.ShowFrontDesk = Pick(nameof(flags.ShowFrontDesk), flags.ShowFrontDesk != false, rec.ShowFrontDesk != false);
        return request;
    }

    public UpdateCheckinSettingsRequest? CheckinRequest(ISet<string> selected)
    {
        if (!selected.Any(k => k.StartsWith("checkin.", StringComparison.Ordinal)))
        {
            return null;
        }

        var wanted = recommended.Checkin;
        return new UpdateCheckinSettingsRequest
        {
            Mode = selected.Contains("checkin.Mode") ? wanted.Mode : checkin.Mode,
            ByDocument = checkin.ByDocument,
            AssumedStayMinutes = selected.Contains("checkin.AssumedStayMinutes") ? wanted.AssumedStayMinutes : checkin.AssumedStayMinutes,
            RemoteTraining = selected.Contains("checkin.RemoteTraining") ? wanted.RemoteTraining : checkin.RemoteTraining == true,
        };
    }

    public UpdateMembershipSettingsRequest? MembershipsRequest(ISet<string> selected)
    {
        if (!selected.Any(k => k.StartsWith("memberships.", StringComparison.Ordinal)))
        {
            return null;
        }

        var wanted = recommended.Memberships;
        return new UpdateMembershipSettingsRequest
        {
            GraceDays = selected.Contains("memberships.GraceDays") ? wanted.GraceDays : memberships.GraceDays,
            ExpiringDays = selected.Contains("memberships.ExpiringDays") ? wanted.ExpiringDays : memberships.ExpiringDays,
            EnrollmentFee = selected.Contains("memberships.EnrollmentFee") ? wanted.EnrollmentFee : memberships.EnrollmentFee,
        };
    }

    public UpdateRoutineSettingsRequest? RoutinesRequest(ISet<string> selected)
        => selected.Contains("routines.ReviewWeeks")
            ? new UpdateRoutineSettingsRequest { ReviewWeeks = recommended.Routines.ReviewWeeks }
            : null;

    private static string OnOff(bool on) => on ? "Encendido" : "Apagado";

    private static string ModeText(string mode)
        => string.Equals(mode, "Static", StringComparison.OrdinalIgnoreCase) ? "Cartel impreso" : "Pantalla en la entrada";
}
