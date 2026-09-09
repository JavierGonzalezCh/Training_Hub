namespace GSM.Application.Identity.Abstractions;

/// <summary>Emite el access token JWT. Implementada en Infrastructure.</summary>
public interface IJwtTokenService
{
    AccessToken CreateAccessToken(AuthenticatedUser user);

    /// <summary>
    /// Token de impersonación de vida corta (15 min). Mantiene <c>sub</c> = superadmin real, lleva
    /// el <c>gym_id</c> visitado y marca <c>impersonating: true</c> + el id de la sesión de auditoría.
    /// </summary>
    AccessToken CreateImpersonationToken(ImpersonationTokenRequest request);
}

public sealed record ImpersonationTokenRequest(
    Guid SuperAdminUserId,
    string SuperAdminName,
    Guid GymId,
    Guid SessionId);

/// <param name="Value">JWT compacto firmado.</param>
/// <param name="ExpiresAtUtc">Instante de expiración del access token.</param>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAtUtc);
