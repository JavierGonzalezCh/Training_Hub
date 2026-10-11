using System.Text.RegularExpressions;
using System.Web;
using GSM.Application.Common.Abstractions;

namespace GSM.IntegrationTests;

/// <summary>Extrae el enlace de restablecer o activar (<c>/restablecer</c>, <c>/activar</c>) de un correo capturado.</summary>
public static class EmailLinks
{
    public sealed record Link(string Url, string Path, string Email, string? Code);

    public static Link Parse(EmailMessage message)
    {
        var match = Regex.Match(message.TextBody ?? string.Empty, @"(?<url>(?:https?://[^/\s]+)?(?<path>/(?:restablecer|activar))\?\S+)");
        if (!match.Success)
        {
            throw new InvalidOperationException($"El correo no trae un enlace: {message.TextBody}");
        }

        var url = match.Groups["url"].Value;
        var query = HttpUtility.ParseQueryString(url[(url.IndexOf('?') + 1)..]);
        return new Link(url, match.Groups["path"].Value, query["email"]!, query["code"]);
    }
}
