using System.ComponentModel.DataAnnotations;

namespace GSM.Application.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; init; } = string.Empty;

    [Required]
    public string Audience { get; init; } = string.Empty;

    /// <summary>Clave simétrica de firma. Mínimo 32 bytes. Nunca en appsettings versionado.</summary>
    [Required]
    [MinLength(32)]
    public string SigningKey { get; init; } = string.Empty;

    /// <summary>Vida del access token. La app lo renueva sola; más largo, menos renovaciones mientras se entrena.</summary>
    [Range(1, 1440)]
    public int AccessTokenMinutes { get; init; } = 60;

    /// <summary>
    /// Vida del refresh token. Cada renovación emite uno nuevo con esta vida, así que la sesión solo vence si la persona
    /// no abre la app en todo ese tiempo.
    /// </summary>
    [Range(1, 365)]
    public int RefreshTokenDays { get; init; } = 90;
}
