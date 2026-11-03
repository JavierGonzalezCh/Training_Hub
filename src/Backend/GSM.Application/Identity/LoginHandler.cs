using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Email;
using GSM.Application.Common.Logs;
using GSM.Application.Gyms.Public;
using GSM.Application.Identity.Abstractions;
using GSM.Application.Identity.Contracts;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Identity;

/// <summary>Bloqueo por intentos (§7): 5 contraseñas incorrectas seguidas bloquean la cuenta 15 minutos.</summary>
public static class LockoutPolicy
{
    public const int MaxFailedAttempts = 5;

    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(15);
}

public sealed class LoginHandler(
    IUserCredentialsService credentials,
    IMemberLoginDirectory memberLogins,
    IEmailSender emailSender,
    SessionIssuer sessions,
    ILogStore logStore,
    ILogger<LoginHandler> logger)
{
    public async Task<LoginResponse> HandleAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Login solicitado para {Identifier}", request.Email);

        // FL1: se entra con el correo o con el celular (10 dígitos); el celular se traduce al correo de la cuenta.
        var email = await ResolveEmailAsync(request.Email.Trim(), cancellationToken);

        // Email inexistente, contraseña mala, cuenta bloqueada o sin accesos: mismo mensaje y mismo código,
        // para no revelar si el email tiene cuenta.
        var check = await credentials.SignInAsync(email, request.Password, cancellationToken);

        if (check.LockedOutNow && check.LockedAccount is { } locked)
        {
            logger.LogWarning("Cuenta {UserId} bloqueada por intentos fallidos", locked.Id);
            await NotifyLockoutAsync(locked, cancellationToken);
        }

        var signedIn = check.Account;

        // FL1: primer ingreso de un miembro sin cuenta, con su documento como clave provisional.
        if (signedIn is null && !check.LockedOutNow && !await credentials.ExistsAsync(email, cancellationToken)
            && await memberLogins.ProvisionFirstLoginAsync(email, request.Password, cancellationToken) is { } newUserId)
        {
            signedIn = await credentials.FindActiveByIdAsync(newUserId, cancellationToken);
            logger.LogInformation("Primer ingreso de la cuenta {UserId}: debe crear su contraseña", newUserId);
        }

        if (signedIn is null)
        {
            // Seguridad (docs/GSM-logs.md §4.7): el email intentado, nunca la contraseña.
            logStore.RecordSecurityEvent(new SecurityEventLogEntry(
                SecurityEventTypes.LoginFailed, $"email={request.Email}"));
            if (check.LockedOutNow)
            {
                logStore.RecordSecurityEvent(new SecurityEventLogEntry(
                    SecurityEventTypes.AccountLocked,
                    $"email={request.Email}; intentos={LockoutPolicy.MaxFailedAttempts}; minutos={(int)LockoutPolicy.Duration.TotalMinutes}"));
            }
        }

        var account = signedIn
            ?? throw new UnauthorizedDomainException("Credenciales inválidas.");

        return await sessions.IssueLoginAsync(account, cancellationToken);
    }

    /// <summary>
    /// Con <c>@</c> es un correo. Si no, se trata como celular: con una sola cuenta (o ficha sin cuenta) detrás,
    /// se usa su correo; si el número no es de nadie, se deja tal cual y el login falla como cualquier otro.
    /// </summary>
    private async Task<string> ResolveEmailAsync(string identifier, CancellationToken cancellationToken)
    {
        if (identifier.Contains('@'))
        {
            return identifier;
        }

        var emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in await memberLogins.FindByPhoneAsync(identifier, cancellationToken))
        {
            var email = candidate.UserId is { } userId
                ? (await credentials.FindActiveByIdAsync(userId, cancellationToken))?.Email
                : candidate.Email;
            if (!string.IsNullOrWhiteSpace(email))
            {
                emails.Add(email.Trim());
            }
        }

        return emails.Count switch
        {
            1 => emails.First(),
            0 => identifier,
            _ => throw new UnauthorizedDomainException(
                "Ese celular está en más de una cuenta. Entra con tu correo."),
        };
    }

    private async Task NotifyLockoutAsync(AccountIdentity account, CancellationToken cancellationToken)
    {
        try
        {
            await emailSender.SendAsync(
                EmailTemplates.AccountLockedOut(
                    account.Email, account.FullName, (int)LockoutPolicy.Duration.TotalMinutes),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo enviar el aviso de bloqueo a la cuenta {UserId}", account.Id);
        }
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
