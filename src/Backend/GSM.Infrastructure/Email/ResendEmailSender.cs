using System.Net.Http.Json;
using GSM.Application.Common.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GSM.Infrastructure.Email;

/// <summary>Envía por la API HTTP de Resend. Un fallo se loguea y no propaga (el email es best-effort).</summary>
public sealed class ResendEmailSender(
    HttpClient httpClient,
    IOptions<ResendOptions> options,
    ILogger<ResendEmailSender> logger) : IEmailSender
{
    private readonly ResendOptions _options = options.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
            {
                Content = JsonContent.Create(new
                {
                    from = _options.FromAddress,
                    to = new[] { message.To },
                    subject = message.Subject,
                    html = message.HtmlBody,
                    text = message.TextBody,
                }),
            };
            request.Headers.Add("Authorization", $"Bearer {_options.ApiKey}");

            var response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError("Resend devolvió {Status} enviando a {To}: {Body}",
                    (int)response.StatusCode, message.To, body);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fallo enviando email a {To}", message.To);
        }
    }
}
