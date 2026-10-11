using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms.Public;
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
    IGymReader gyms,
    ILogger<SessionIssuer> logger)
{
    /// <summary>
    /// Lo que sigue a validar la identidad de una persona (contraseña correcta, enlace de activación): el
    /// SuperAdmin entra directo; con un solo acceso se emite la sesión; con varios, devuelve el boleto y la
    /// lista para elegir gym.
    /// </summary>
    public async Task<LoginResponse> IssueLoginAsync(AccountIdentity account, CancellationToken cancellationToken)
    {
        if (account.IsSuperAdmin)
        {
            logger.LogInformation("Login correcto para el superadmin {UserId}", account.Id);
            return new LoginResponse(false, await IssueForSuperAdminAsync(account, cancellationToken));
        }

        var active = await accesses.ListActiveAsync(account.Id, cancellationToken);

        // Sin ningún acceso activo (p. ej. staff desactivado) la cuenta no entra a ningún gym.
        if (active.Count == 0)
        {
            throw new UnauthorizedDomainException("Credenciales inválidas.");
        }

        if (active.Count == 1)
        {
            return new LoginResponse(false, await IssueForAccessAsync(account, active[0], cancellationToken));
        }

        // Varios accesos: la persona elige. El boleto prueba que ya validó su identidad.
        var options = new List<GymAccessOption>(active.Count);
        foreach (var access in active)
        {
            var branding = await gyms.GetBrandingAsync(access.GymId, cancellationToken);
            options.Add(new GymAccessOption(
                access.Id,
                access.GymId,
                branding?.Name ?? "Gimnasio",
                branding?.LogoUrl,
                RoleName.ToClaim(access.Role),
                access.LastUsedAt));
        }

        var ticket = jwtTokens.CreateGymSelectionTicket(account.Id);
        logger.LogInformation("Login de {UserId}: {Count} accesos, falta elegir gym", account.Id, options.Count);

        return new LoginResponse(true, SelectionToken: ticket.Value, Accesses: options);
    }

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

        var permissions = await accesses.GetEffectivePermissionsAsync(access, cancellationToken);
        var user = new AuthenticatedUser(
            account.Id, account.Email, account.FullName, access.Role, access.GymId, permissions);
        var accessToken = jwtTokens.CreateAccessToken(user);
        var refreshToken = await refreshTokens.IssueAsync(account.Id, access.Id, cancellationToken);
        await accesses.MarkUsedAsync(access.Id, clock.UtcNow, cancellationToken);

        logger.LogInformation(
            "Sesión emitida para {UserId} en el gym {GymId} con rol {Role}", account.Id, access.GymId, access.Role);

        return new AuthTokensResponse(accessToken.Value, refreshToken, accessToken.ExpiresAtUtc);
    }
}
