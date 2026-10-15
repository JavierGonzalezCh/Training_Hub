namespace GSM.Application.Common.Abstractions;

/// <summary>Envío de email transaccional. Implementado sobre Resend (o un logger en desarrollo).</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <param name="Sensitive">
/// El cuerpo lleva un secreto (p. ej. un código de acceso): los senders que lo registran en el log no
/// deben volcar el contenido.
/// </param>
public sealed record EmailMessage(
    string To, string Subject, string HtmlBody, string? TextBody = null, bool Sensitive = false);
