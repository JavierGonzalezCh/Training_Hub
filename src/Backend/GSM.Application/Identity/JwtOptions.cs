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

    [Range(1, 1440)]
    public int AccessTokenMinutes { get; init; } = 60;

    [Range(1, 365)]
    public int RefreshTokenDays { get; init; } = 30;
}
