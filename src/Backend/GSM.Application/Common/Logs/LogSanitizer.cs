using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GSM.Application.Common.Logs;

/// <summary>
/// Limpieza de datos sensibles antes de guardar (docs/GSM-logs.md §3): por lista de nombres de campo, nunca
/// se guardan contraseñas, tokens, códigos, firmas ni datos de salud. Cualquier nombre que <b>contenga</b>
/// uno de los fragmentos se redacta (es conservador a propósito: ante la duda, se oculta).
/// </summary>
public static class LogSanitizer
{
    public const string Redacted = "[REDACTED]";
    public const int RequestSummaryMaxChars = 4096;

    /// <summary>Fragmentos (sin distinguir mayúsculas) que marcan un campo como sensible.</summary>
    private static readonly string[] SensitiveFragments =
    [
        "password", "passwd", "token", "secret", "code", "signature", "authorization", "apikey", "api_key",
        "medical", "medication", "restriction", "health", "allerg", "injur", "condition",
    ];

    public static bool IsSensitive(string name)
    {
        foreach (var fragment in SensitiveFragments)
        {
            if (name.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Redacta los campos sensibles de un JSON. Si el texto no es JSON válido se descarta entero (no se puede
    /// garantizar que esté limpio). No recorta: el llamador decide el límite.
    /// </summary>
    public static string SanitizeJson(string json)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return "\"[unparseable body omitted]\"";
        }

        Redact(node);
        return node?.ToJsonString() ?? "null";
    }

    /// <summary>Limpia y recorta a <see cref="RequestSummaryMaxChars"/> el cuerpo de una petición.</summary>
    public static string SanitizeBodyForSummary(string body) => Truncate(SanitizeJson(body), RequestSummaryMaxChars);

    private static readonly Regex Email = new(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);
    private static readonly Regex Bearer = new(@"Bearer\s+[A-Za-z0-9._~+/=\-]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Jwt = new(@"eyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]*", RegexOptions.Compiled);
    private static readonly Regex PemKey = new(@"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?(-----END [A-Z ]*PRIVATE KEY-----|$)", RegexOptions.Compiled);
    private static readonly Regex KeyValue = new(
        @"(?<k>[\w\-]*(password|passwd|token|secret|code|signature|authorization|apikey|api_key)[\w\-]*)(?<sep>\s*[=:]\s*)(?<v>""[^""]*""|'[^']*'|[^\s&,;""']+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Limpia texto libre (mensajes, trazas, cuerpos de respuesta de proveedores): si es JSON redacta por nombre de
    /// campo; si no, oculta tokens Bearer, JWT, claves PEM, parejas <c>nombre=valor</c> con nombre sensible y correos.
    /// No recorta.
    /// </summary>
    public static string SanitizeText(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            var json = SanitizeJson(text);
            if (!json.StartsWith("\"[unparseable", StringComparison.Ordinal))
            {
                return MaskEmails(json);
            }
        }

        var clean = PemKey.Replace(text, Redacted);
        clean = Bearer.Replace(clean, "Bearer " + Redacted);
        clean = Jwt.Replace(clean, Redacted);
        clean = KeyValue.Replace(clean, m => m.Groups["k"].Value + m.Groups["sep"].Value + Redacted);
        return MaskEmails(clean);
    }

    /// <summary>Quita la consulta y el fragmento de una ruta (<c>/app/registro?code=…</c> pasa a <c>/app/registro</c>).</summary>
    public static string StripQuery(string route)
    {
        var cut = route.IndexOfAny(['?', '#']);
        return cut < 0 ? route : route[..cut];
    }

    private static string MaskEmails(string text) => Email.Replace(text, "[email]");

    /// <summary>Redacta el valor de una pareja nombre/valor (parámetro de ruta o de consulta).</summary>
    public static string SanitizeValue(string name, string? value) => IsSensitive(name) ? Redacted : value ?? string.Empty;

    public static string Truncate(string text, int maxChars) =>
        text.Length <= maxChars ? text : text[..maxChars] + "…[truncated]";

    private static void Redact(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var property in obj.ToList())
                {
                    if (IsSensitive(property.Key))
                    {
                        obj[property.Key] = Redacted;
                    }
                    else
                    {
                        Redact(property.Value);
                    }
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    Redact(item);
                }

                break;
        }
    }
}
