namespace GSM.Application.Identity.Abstractions;

/// <summary>Emite el access token JWT. Implementada en Infrastructure.</summary>
public interface IJwtTokenService
{
    AccessToken CreateAccessToken(AuthenticatedUser user);
}

/// <param name="Value">JWT compacto firmado.</param>
/// <param name="ExpiresAtUtc">Instante de expiración del access token.</param>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAtUtc);
