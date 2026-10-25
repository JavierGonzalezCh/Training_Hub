using GSM.Client.Generated;
using MudBlazor;

namespace GSM.Panel.UI.Pages.Communications;

public static class AnnouncementActions
{
    /// <summary>
    /// Pide confirmación antes de publicar cuando el anuncio llega lejos o no se puede deshacer: por email (los
    /// correos no se retiran), importante (push que se salta el horario sin avisos y las preferencias) o para todos
    /// los miembros. Un anuncio segmentado, normal y sin email se publica directo. El mensaje dice a cuántos y por
    /// qué canales, con los conteos del servidor (CO2-4).
    /// </summary>
    public static async Task<bool> ConfirmPublishAsync(
        IDialogService dialogs, IAnnouncementsClient announcements, bool sendEmail, bool important, int category,
        AnnouncementAudienceDto? audience)
    {
        var toEveryone = IsEveryone(audience);
        if (!sendEmail && !important && !toEveryone)
        {
            return true;
        }

        var reach = await CountAsync(() => announcements.AudiencePreviewAsync(audience ?? Empty()), r => r.Count);
        var lines = new List<string>
        {
            reach < 0
                ? (toEveryone ? "Llegará a todos los miembros activos en la app." : "Llegará a los miembros del segmento en la app.")
                : $"Llegará a {Members(reach)}{(toEveryone ? ", todos los activos," : string.Empty)} en la app.",
        };

        if (important)
        {
            lines.Add("Es importante: les llega por push aunque tengan apagadas las novedades y aunque sea horario sin avisos.");
        }

        if (sendEmail)
        {
            var emails = await CountAsync(
                () => announcements.EmailAudienceAsync(new EmailAudienceRequest { Category = category, Audience = audience }),
                r => r.Recipients);
            lines.Add(emails < 0
                ? "También se enviará por email. Los correos no se pueden retirar después."
                : $"También se enviará por email a {Members(emails)}. Los correos no se pueden retirar después.");
        }

        var title = important ? "Publicar anuncio importante" : sendEmail ? "Publicar y enviar por email" : "Publicar para todos";
        var confirmed = await dialogs.ShowMessageBox(
            title,
            string.Join(" ", lines),
            yesText: reach > 0 ? $"Publicar a {Members(reach)}" : "Publicar",
            cancelText: "Volver");
        return confirmed == true;
    }

    /// <summary>Sin ningún filtro, el anuncio va a todos los miembros activos.</summary>
    private static bool IsEveryone(AnnouncementAudienceDto? audience)
        => audience is null
           || ((audience.Statuses?.Count ?? 0) == 0 && (audience.PlanIds?.Count ?? 0) == 0
               && (audience.ClassIds?.Count ?? 0) == 0 && (audience.Tags?.Count ?? 0) == 0);

    private static AnnouncementAudienceDto Empty() => new() { Statuses = [], PlanIds = [], ClassIds = [], Tags = [] };

    private static string Members(int n) => n == 1 ? "1 miembro" : $"{n} miembros";

    /// <summary>Un conteo que falla no impide publicar: se confirma sin el número (-1).</summary>
    private static async Task<int> CountAsync<T>(Func<Task<T>> load, Func<T, int> count)
    {
        try
        {
            return count(await load());
        }
        catch (GsmApiException)
        {
            return -1;
        }
    }
}
