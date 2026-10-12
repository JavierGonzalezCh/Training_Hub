using GSM.Application.Notifications;
using GSM.Application.Notifications.Abstractions;
using GSM.Application.Notifications.Contracts;
using Microsoft.Extensions.Options;

namespace GSM.Infrastructure.Notifications.Push;

/// <summary>
/// Entrega a la app web los datos públicos de Firebase. Habilitado solo si el envío está configurado y el
/// cliente web tiene sus datos completos; si no, la app no ofrece activar los avisos.
/// </summary>
public sealed class PushWebConfigProvider(IOptions<FcmOptions> options, IPushSender sender) : IPushWebConfigProvider
{
    public PushConfigResponse Get()
    {
        var o = options.Value;
        var web = o.Web;
        var complete = sender.IsConfigured
            && !string.IsNullOrWhiteSpace(web.ApiKey)
            && !string.IsNullOrWhiteSpace(web.AppId)
            && !string.IsNullOrWhiteSpace(web.MessagingSenderId)
            && !string.IsNullOrWhiteSpace(web.VapidKey);
        return complete
            ? new PushConfigResponse(true, o.ProjectId, web.ApiKey, web.AppId, web.MessagingSenderId, web.VapidKey)
            : new PushConfigResponse(false, null, null, null, null, null);
    }
}
