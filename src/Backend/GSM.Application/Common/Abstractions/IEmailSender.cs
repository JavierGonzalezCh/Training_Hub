namespace GSM.Application.Common.Abstractions;

/// <summary>Envío de email transaccional. Implementado sobre Resend (o un logger en desarrollo).</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public sealed record EmailMessage(string To, string Subject, string HtmlBody, string? TextBody = null);
