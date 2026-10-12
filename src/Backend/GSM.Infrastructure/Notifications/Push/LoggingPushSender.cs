using GSM.Application.Notifications.Abstractions;
using Microsoft.Extensions.Logging;

namespace GSM.Infrastructure.Notifications.Push;

/// <summary>
/// Push sin Firebase configurado: no envía nada y lo deja en el log. Nunca falla, para que la API arranque
/// y el dispatcher despache igual (mismo criterio que <c>LoggingEmailSender</c>).
/// </summary>
public sealed class LoggingPushSender(ILogger<LoggingPushSender> logger) : IPushSender
{
    public bool IsConfigured => false;

    public Task<PushSendResult> SendAsync(PushMessage message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "PUSH (no enviado, Firebase sin configurar) → aviso {NotificationId} | {Platform} | {Title}",
            message.NotificationId, message.Platform, message.Title);
        return Task.FromResult(PushSendResult.NotConfigured);
    }
}
