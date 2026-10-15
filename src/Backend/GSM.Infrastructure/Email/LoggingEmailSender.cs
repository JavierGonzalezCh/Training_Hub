using GSM.Application.Common.Abstractions;
using Microsoft.Extensions.Logging;

namespace GSM.Infrastructure.Email;

/// <summary>Sender de desarrollo: registra el email en el log en vez de enviarlo. Nunca falla.</summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "EMAIL (no enviado) → {To} | {Subject}\n{Body}",
            message.To, message.Subject,
            message.Sensitive ? "(contenido omitido: lleva un código)" : message.TextBody ?? message.HtmlBody);
        return Task.CompletedTask;
    }
}
