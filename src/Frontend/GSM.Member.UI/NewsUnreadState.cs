using GSM.Client.Generated;

namespace GSM.Member.UI;

/// <summary>
/// No leídos de "Novedades" (anuncios nuevos + avisos sin leer), compartidos entre la pestaña de la
/// barra inferior y la propia página, que lo refresca al marcar como leído.
/// </summary>
public sealed class NewsUnreadState(IMeClient me)
{
    public int Total { get; private set; }

    public event Action? Changed;

    public async Task RefreshAsync()
    {
        try
        {
            var unread = await me.UnreadCountAsync();
            var total = unread.Announcements + unread.Notifications;
            if (total != Total)
            {
                Total = total;
                Changed?.Invoke();
            }
        }
        catch (GsmApiException)
        {
            // Con el módulo apagado (403) o sin red, el indicador simplemente no se muestra.
        }
    }
}
