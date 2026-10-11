using System.ComponentModel.DataAnnotations;

namespace GSM.Infrastructure.Communications;

public sealed class CommunicationsOptions
{
    public const string SectionName = "Communications";

    /// <summary>Clave HMAC del enlace de baja. Mínimo 32 caracteres; distinta de las demás.</summary>
    [Required]
    [MinLength(32)]
    public string UnsubscribeSigningKey { get; init; } = string.Empty;

    /// <summary>URL pública de la API, base del enlace de baja (p. ej. <c>https://api.dominio</c>).</summary>
    [Required]
    [Url]
    public string PublicApiBaseUrl { get; init; } = string.Empty;
}
