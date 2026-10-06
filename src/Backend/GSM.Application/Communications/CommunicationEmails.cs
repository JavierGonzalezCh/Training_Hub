using System.Net;

namespace GSM.Application.Communications;

/// <summary>
/// Plantillas de los correos de comunicaciones: HTML mínimo y su versión en texto, con el gym
/// como remitente visible y el enlace de baja al pie. Voz del producto: directa, sin relleno.
/// </summary>
internal static class CommunicationEmails
{
    public static (string Subject, string Html, string Text) Compose(
        string gymName, string firstName, string title, string body, string unsubscribeUrl)
    {
        var gym = WebUtility.HtmlEncode(gymName);
        var paragraphs = string.Join(
            string.Empty,
            body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => $"<p>{WebUtility.HtmlEncode(line.TrimEnd('\r'))}</p>"));

        var html = $"""
            <p>Hola {WebUtility.HtmlEncode(firstName)},</p>
            <h2 style="font-size:18px;margin:16px 0 8px">{WebUtility.HtmlEncode(title)}</h2>
            {paragraphs}
            <hr style="border:none;border-top:1px solid #ddd;margin:24px 0">
            <p style="font-size:12px;color:#666">Te escribe {gym} a través de GSM.
            <a href="{WebUtility.HtmlEncode(unsubscribeUrl)}">No quiero recibir más avisos por email</a>.</p>
            """;

        var text = $"""
            Hola {firstName},

            {title}

            {body}

            —
            Te escribe {gymName} a través de GSM.
            Para no recibir más avisos por email: {unsubscribeUrl}
            """;

        return ($"{gymName}: {title}", html, text);
    }
}
