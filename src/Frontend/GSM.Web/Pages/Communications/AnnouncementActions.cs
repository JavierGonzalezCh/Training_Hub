using GSM.Client.Generated;
using MudBlazor;

namespace GSM.Web.Pages.Communications;

internal static class AnnouncementActions
{
    /// <summary>
    /// Pide confirmación antes de publicar si el anuncio va por email: los correos no se pueden
    /// retirar. Sin email se publica directamente.
    /// </summary>
    public static async Task<bool> ConfirmPublishAsync(IDialogService dialogs, IAnnouncementsClient announcements, bool sendEmail)
    {
        if (!sendEmail)
        {
            return true;
        }

        int recipients;
        try
        {
            recipients = (await announcements.EmailAudienceAsync()).Recipients;
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
