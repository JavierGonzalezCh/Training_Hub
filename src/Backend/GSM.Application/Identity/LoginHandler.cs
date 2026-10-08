using GSM.Application.Gyms.Public;
using GSM.Application.Identity.Abstractions;
using GSM.Application.Identity.Contracts;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Identity;

public sealed class LoginHandler(
    IUserCredentialsService credentials,
    IGymAccessService accesses,
    IGymReader gyms,
    IJwtTokenService jwtTokens,
    SessionIssuer sessions,
    ILogger<LoginHandler> logger)
{
    public async Task<LoginResponse> HandleAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Login solicitado para {Email}", request.Email);

        var account = await credentials.ValidateCredentialsAsync(request.Email, request.Password, cancellationToken)
            ?? throw new UnauthorizedDomainException("Credenciales inválidas.");

        if (account.IsSuperAdmin)
        {
            logger.LogInformation("Login correcto para el superadmin {UserId}", account.Id);
            return new LoginResponse(false, await sessions.IssueForSuperAdminAsync(account, cancellationToken));
        }

        var active = await accesses.ListActiveAsync(account.Id, cancellationToken);

        // Sin ningún acceso activo (p. ej. staff desactivado) la cuenta no entra a ningún gym.
        if (active.Count == 0)
        {
            throw new UnauthorizedDomainException("Credenciales inválidas.");
        }

        if (active.Count == 1)
        {
            return new LoginResponse(false, await sessions.IssueForAccessAsync(account, active[0], cancellationToken));
        }

        // Varios accesos: la persona elige. El boleto prueba que ya validó su contraseña.
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
}

/// <summary>Segundo paso del login con varios accesos: con el boleto y el acceso elegido emite la sesión.</summary>
public sealed class SelectGymHandler(
    IUserCredentialsService credentials,
    IGymAccessService accesses,
    IJwtTokenService jwtTokens,
    SessionIssuer sessions,
    ILogger<SelectGymHandler> logger)
{
    public async Task<AuthTokensResponse> HandleAsync(SelectGymRequest request, CancellationToken cancellationToken)
    {
        var userId = await jwtTokens.ValidateGymSelectionTicketAsync(request.SelectionToken)
            ?? throw new UnauthorizedDomainException("La selección de gym venció. Inicia sesión de nuevo.");

        var account = await credentials.FindActiveByIdAsync(userId, cancellationToken)
            ?? throw new UnauthorizedDomainException("La cuenta no está disponible.");

        var access = await accesses.GetActiveAsync(userId, request.AccessId, cancellationToken)
            ?? throw new ForbiddenDomainException("No tienes un acceso activo a ese gimnasio.");

        logger.LogInformation("Selección de gym: {UserId} elige el acceso {AccessId}", userId, access.Id);
        return await sessions.IssueForAccessAsync(account, access, cancellationToken);
    }
}

/// <summary>
/// "Cambiar de gym" / "pasar a modo miembro o staff": emite la sesión de otro acceso activo de la misma
/// persona sin pedir contraseña (la sesión actual ya prueba quién es).
/// </summary>
public sealed class SwitchGymHandler(
    Common.Abstractions.ICurrentUserService currentUser,
    IUserCredentialsService credentials,
    IGymAccessService accesses,
    IRefreshTokenService refreshTokens,
    SessionIssuer sessions,
    ILogger<SwitchGymHandler> logger)
{
    public async Task<AuthTokensResponse> HandleAsync(SwitchGymRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId
            ?? throw new UnauthorizedDomainException("No hay usuario autenticado en el contexto.");

        // Un superadmin (también impersonando) no tiene accesos: no hay a dónde cambiar.
        if (currentUser.IsSuperAdmin)
        {
            throw new ForbiddenDomainException("El superadmin no cambia de gym: usa la impersonación.");
        }

        var account = await credentials.FindActiveByIdAsync(userId, cancellationToken)
            ?? throw new UnauthorizedDomainException("La cuenta no está disponible.");

        var access = await accesses.GetActiveAsync(userId, request.AccessId, cancellationToken)
            ?? throw new ForbiddenDomainException("No tienes un acceso activo a ese gimnasio.");

        var tokens = await sessions.IssueForAccessAsync(account, access, cancellationToken);

        // La sesión que se deja no debe seguir renovándose.
        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            await refreshTokens.RevokeOwnedAsync(request.RefreshToken, userId, cancellationToken);
        }

        logger.LogInformation("Cambio de gym: {UserId} pasa al acceso {AccessId}", userId, access.Id);
        return tokens;
    }
}

/// <summary>Los accesos activos de la persona autenticada, para el menú "Cambiar de gym".</summary>
public sealed class ListMyAccessesHandler(
    Common.Abstractions.ICurrentUserService currentUser,
    IGymAccessService accesses,
    IGymReader gyms)
{
    public async Task<IReadOnlyList<GymAccessOption>> HandleAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId
            ?? throw new UnauthorizedDomainException("No hay usuario autenticado en el contexto.");

        // El SuperAdmin no tiene accesos.
        if (currentUser.IsSuperAdmin)
        {
            return [];
        }

        var active = await accesses.ListActiveAsync(userId, cancellationToken);
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
                access.LastUsedAt,
                IsCurrent: access.GymId == currentUser.GymId && access.Role == currentUser.Role));
        }

        return options;
    }
}

/// <summary>
/// Inicia la sesión de quien acaba de registrarse como miembro (el registro devuelve tokens). Lo compone
/// el controller después de que el canje de la invitación confirmó: Members no emite sesiones.
/// </summary>
public sealed class StartMemberSessionHandler(
    IUserCredentialsService credentials,
    IGymAccessService accesses,
    SessionIssuer sessions)
{
    public async Task<AuthTokensResponse> HandleAsync(Guid userId, Guid gymId, CancellationToken cancellationToken)
    {
        var account = await credentials.FindActiveByIdAsync(userId, cancellationToken)
            ?? throw new UnauthorizedDomainException("La cuenta no está disponible.");

        var access = await accesses.FindActiveAsync(userId, gymId, Domain.Identity.UserRole.Member, cancellationToken)
            ?? throw new ForbiddenDomainException("No tienes un acceso activo a ese gimnasio.");

        return await sessions.IssueForAccessAsync(account, access, cancellationToken);
    }
}
