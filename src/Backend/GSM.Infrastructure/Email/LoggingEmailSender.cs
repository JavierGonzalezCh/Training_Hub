using GSM.Application.Common.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GSM.Infrastructure.Email;

/// <summary>
/// Sender sin proveedor: registra el email en el log en vez de enviarlo. Nunca falla. Un correo sensible (lleva un
/// código) solo muestra su contenido en Development (I5-3), para poder entrar en local sin un correo real; en
/// cualquier otro ambiente el código nunca llega al log.
/// </summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger, IHostEnvironment environment) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var showBody = !message.Sensitive || environment.IsDevelopment();
        logger.LogInformation(
            "EMAIL (no enviado) → {To} | {Subject}\n{Body}",
            message.To, message.Subject,
            showBody ? message.TextBody ?? message.HtmlBody : "(contenido omitido: lleva un código)");
        return Task.CompletedTask;
    }
}
