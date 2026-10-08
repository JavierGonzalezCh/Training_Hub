using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.SuperAdmin;

/// <summary>
/// Una petición hecha durante una sesión de impersonación: cada consulta y cada escritura, con el
/// endpoint por su plantilla de ruta (nunca con ids ni query string). Solo la ve el SuperAdmin.
/// </summary>
public sealed class ImpersonationAction : BaseEntity
{
    public const int MaxMethodLength = 10;
    public const int MaxEndpointLength = 300;
    public const int MaxSummaryLength = 300;

    private ImpersonationAction()
    {
    }

    public Guid SessionId { get; private set; }

    public string Method { get; private set; } = string.Empty;

    /// <summary>Plantilla de la ruta, p. ej. <c>api/members/{id}</c>.</summary>
    public string Endpoint { get; private set; } = string.Empty;

    /// <summary>Qué se hizo, en corto, p. ej. <c>Payments.Register</c>.</summary>
    public string Summary { get; private set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; private set; }

    public int StatusCode { get; private set; }

    public static ImpersonationAction Record(
        Guid sessionId, string method, string endpoint, string summary, int statusCode, DateTimeOffset occurredAt)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ValidationDomainException("La acción necesita su sesión de impersonación.");
        }

        return new ImpersonationAction
        {
            SessionId = sessionId,
            Method = Clip(method, MaxMethodLength),
            Endpoint = Clip(endpoint, MaxEndpointLength),
            Summary = Clip(summary, MaxSummaryLength),
            StatusCode = statusCode,
            OccurredAt = occurredAt,
        };
    }

    private static string Clip(string? value, int max)
    {
        var text = value ?? string.Empty;
        return text.Length <= max ? text : text[..max];
    }
}
