using GSM.Client.Generated;
using MudBlazor;

namespace GSM.Panel.UI.Pages.Communications;

internal static class AnnouncementActions
{
    /// <summary>
    /// Pide confirmación antes de publicar si el anuncio va por email: los correos no se pueden
    /// retirar. Sin email se publica directamente. El conteo usa la categoría y la audiencia del anuncio (CO2-4).
    /// </summary>
    public static async Task<bool> ConfirmPublishAsync(
        IDialogService dialogs, IAnnouncementsClient announcements, bool sendEmail, int category,
        AnnouncementAudienceDto? audience)
    {
        if (!sendEmail)
        {
            return true;
        }

        int recipients;
        try
        {
            recipients = (await announcements.EmailAudienceAsync(
                new EmailAudienceRequest { Category = category, Audience = audience })).Recipients;
        }
        catch (GsmApiException)
        {
            recipients = -1;
        }

        var who = recipients switch
        {
            < 0 => "a tus miembros con email",
            1 => "a 1 miembro",
            _ => $"a {recipients} miembros",
        };

        var confirmed = await dialogs.ShowMessageBox(
            "Publicar y enviar por email",
            $"Se enviará {who}. Los correos no se pueden retirar después.",
            yesText: "Publicar", cancelText: "Cancelar");
        return confirmed == true;
    }
}
