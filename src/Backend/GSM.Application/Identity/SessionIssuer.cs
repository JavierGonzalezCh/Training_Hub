using GSM.Application.Common.Abstractions;
using GSM.Application.Identity.Abstractions;
using GSM.Application.Identity.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Identity;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Identity;

/// <summary>
/// Emite una sesión (access token + refresh token) para una persona en un acceso concreto. Lo comparten
/// el login, la selección de gym, el cambio de gym, la renovación y el registro del miembro, para que
/// el bloqueo por gym suspendido y el "último usado" se apliquen igual en todos.
/// </summary>
public sealed class SessionIssuer(
    IClock clock,
    IJwtTokenService jwtTokens,
    IRefreshTokenService refreshTokens,
    IGymAccessService accesses,
    IGymLoginGate loginGate,
    ILogger<SessionIssuer> logger)
{
    /// <summary>Sesión del SuperAdmin: sin gym.</summary>
    public async Task<AuthTokensResponse> IssueForSuperAdminAsync(
        AccountIdentity account, CancellationToken cancellationToken)
    {
        var user = new AuthenticatedUser(account.Id, account.Email, account.FullName, UserRole.SuperAdmin, null);
        var accessToken = jwtTokens.CreateAccessToken(user);
        var refreshToken = await refreshTokens.IssueAsync(account.Id, null, cancellationToken);
        return new AuthTokensResponse(accessToken.Value, refreshToken, accessToken.ExpiresAtUtc);
    }

    /// <summary>Sesión de la persona en ese acceso. 403 si el gym está suspendido.</summary>
    public async Task<AuthTokensResponse> IssueForAccessAsync(
        AccountIdentity account, GymAccessRecord access, CancellationToken cancellationToken)
    {
        // El bloqueo por estado vive en un filtro de la API, pero el login es anónimo y el filtro
        // no puede cubrirlo: un gym `suspended` no deja entrar ni a sus usuarios.
        if (await loginGate.IsSuspendedAsync(access.GymId, cancellationToken))
        {
            logger.LogWarning("Sesión rechazada para {UserId}: gym {GymId} suspendido", account.Id, access.GymId);
            throw new ForbiddenDomainException("La cuenta del gimnasio está suspendida. Contacta con soporte.");
        }

        var user = new AuthenticatedUser(account.Id, account.Email, account.FullName, access.Role, access.GymId);
        var accessToken = jwtTokens.CreateAccessToken(user);
        var refreshToken = await refreshTokens.IssueAsync(account.Id, access.Id, cancellationToken);
        await accesses.MarkUsedAsync(access.Id, clock.UtcNow, cancellationToken);

        logger.LogInformation(
            "Sesión emitida para {UserId} en el gym {GymId} con rol {Role}", account.Id, access.GymId, access.Role);

        return new AuthTokensResponse(accessToken.Value, refreshToken, accessToken.ExpiresAtUtc);
    }
}
