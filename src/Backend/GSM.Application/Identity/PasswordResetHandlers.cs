using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Email;
using GSM.Application.Identity.Abstractions;
using GSM.Application.Gyms.Public;
using GSM.Application.Identity.Contracts;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Identity;

public sealed class ForgotPasswordHandler(
    IPasswordResetService passwordReset,
    IAccountLinkBuilder links,
    IEmailSender emailSender,
    ILogger<ForgotPasswordHandler> logger)
{
    public async Task HandleAsync(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        var ticket = await passwordReset.CreateTokenAsync(email, cancellationToken);

        if (ticket is null)
        {
            logger.LogInformation("Restablecimiento solicitado para un email sin cuenta activa");
            return; // Sin revelar si el email existe.
        }

        await emailSender.SendAsync(
            EmailTemplates.PasswordReset(ticket.Email, links.ResetUrl(ticket.Email, ticket.Token)), cancellationToken);
        logger.LogInformation("Enlace de restablecimiento enviado a {UserEmail}", ticket.Email);
    }
}

/// <summary>Restablecer por el enlace del correo. Cierra todas las sesiones de la persona y levanta el bloqueo.</summary>
public sealed class ResetPasswordHandler(IPasswordResetService passwordReset)
{
    public Task HandleAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
        => passwordReset.ResetAsync(
            request.Email.Trim(), request.Token, request.NewPassword, cancellationToken);
}

/// <summary>
/// Activar una cuenta invitada con el enlace <c>/activar</c>: crea la contraseña y deja la sesión iniciada
/// (con varios accesos, la persona elige gym como en el login).
/// </summary>
public sealed class ActivateAccountHandler(
    IPasswordResetService passwordReset,
    SessionIssuer sessions,
    ILogger<ActivateAccountHandler> logger)
{
    public async Task<LoginResponse> HandleAsync(ActivateAccountRequest request, CancellationToken cancellationToken)
    {
        var account = await passwordReset.ActivateAsync(
            request.Email.Trim(), request.Code, request.NewPassword, cancellationToken);

        logger.LogInformation("Cuenta {UserId} activada con el enlace de invitación", account.Id);
        return await sessions.IssueLoginAsync(account, cancellationToken);
    }
}

/// <summary>
/// <c>POST /api/me/password</c>: cambiar la contraseña con la sesión iniciada (§11). Cierra las demás
/// sesiones de la persona, en todos sus gyms; la actual se identifica por el refresh token que manda el
/// cliente (el access token no distingue sesiones).
/// </summary>
public sealed class ChangePasswordHandler(
    ICurrentUserService currentUser,
    IUserCredentialsService credentials,
    IRefreshTokenService refreshTokens,
    IEmailSender emailSender,
    ILogger<ChangePasswordHandler> logger)
{
    public async Task HandleAsync(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId
            ?? throw new UnauthorizedDomainException("No hay usuario autenticado en el contexto.");

        // Quien impersona ve el panel de otro gym con su propio token: no puede tocar su contraseña desde ahí.
        if (currentUser.IsImpersonating)
        {
            throw new ForbiddenDomainException("No se puede cambiar la contraseña mientras se impersona un gimnasio.");
        }

        var outcome = await credentials.ChangePasswordAsync(
            userId, request.CurrentPassword, request.NewPassword, cancellationToken);

        if (!outcome.Succeeded)
        {
            if (outcome.LockedOutNow)
            {
                logger.LogWarning("Cuenta {UserId} bloqueada por contraseñas actuales incorrectas al cambiarla", userId);
                await NotifyLockoutAsync(userId, cancellationToken);
            }

            throw new ValidationDomainException(
                outcome.WrongCurrentPassword
                    ? "La contraseña actual no es correcta."
                    : outcome.Error ?? "No se pudo cambiar la contraseña.");
        }

        await refreshTokens.RevokeAllForUserAsync(userId, request.RefreshToken, cancellationToken);
        logger.LogInformation("Contraseña cambiada por {UserId}; las otras sesiones se cerraron", userId);
    }

    private async Task NotifyLockoutAsync(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            var account = await credentials.FindActiveByIdAsync(userId, cancellationToken);
            if (account is not null)
            {
                await emailSender.SendAsync(
                    EmailTemplates.AccountLockedOut(
                        account.Email, account.FullName, (int)LockoutPolicy.Duration.TotalMinutes),
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo enviar el aviso de bloqueo a la cuenta {UserId}", userId);
        }
    }
}

/// <summary>"Copiar enlace" de un staff (solo el Owner, lo impone el controller).</summary>
public sealed class CopyStaffLinkHandler(
    ICurrentUserService currentUser,
    IAccountLinkService links,
    ILogger<CopyStaffLinkHandler> logger)
{
    public async Task<AccountLinkResponse> HandleAsync(Guid userId, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedDomainException("No hay usuario autenticado en el contexto.");

        var link = await links.CopyLinkAsync(gymId, userId, Domain.Identity.UserRole.Staff, actorId, cancellationToken);
        logger.LogInformation("Enlace del staff {UserId} copiado por {ActorId} en el gym {GymId}", userId, actorId, gymId);
        return new AccountLinkResponse(link.Url, link.RequiresLogin, link.ExpiresAt);
    }
}

/// <summary>Los Owners de un gym con su estado de activación (ficha del SuperAdmin).</summary>
public sealed class ListGymOwnersHandler(IAccountLinkService links)
{
    public async Task<IReadOnlyList<GymOwnerResponse>> HandleAsync(Guid gymId, CancellationToken cancellationToken)
        => (await links.ListOwnersAsync(gymId, cancellationToken))
            .Select(o => new GymOwnerResponse(o.UserId, o.Email, o.FullName, o.IsActive, o.HasPassword))
            .ToList();
}

/// <summary>"Copiar enlace" de un Owner (solo el SuperAdmin, lo impone el controller).</summary>
public sealed class CopyOwnerLinkHandler(
    ICurrentUserService currentUser,
    IAccountLinkService links,
    ILogger<CopyOwnerLinkHandler> logger)
{
    public async Task<AccountLinkResponse> HandleAsync(Guid gymId, Guid userId, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedDomainException("No hay usuario autenticado en el contexto.");

        var link = await links.CopyLinkAsync(gymId, userId, Domain.Identity.UserRole.Owner, actorId, cancellationToken);
        logger.LogInformation("Enlace del owner {UserId} copiado por {ActorId} en el gym {GymId}", userId, actorId, gymId);
        return new AccountLinkResponse(link.Url, link.RequiresLogin, link.ExpiresAt);
    }
}

/// <summary>
/// "Reenviar acceso" de un Owner (solo el SuperAdmin): genera un enlace nuevo, que anula los anteriores, y lo
/// manda por correo (Gyms §4). Si el correo falla, el enlace se copia con "Copiar enlace".
/// </summary>
public sealed class ResendOwnerAccessHandler(
    ICurrentUserService currentUser,
    IAccountLinkService links,
    IGymReader gyms,
    IEmailSender emailSender,
    ILogger<ResendOwnerAccessHandler> logger)
{
    public async Task HandleAsync(Guid gymId, Guid userId, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedDomainException("No hay usuario autenticado en el contexto.");

        var gym = await gyms.GetAdminViewAsync(gymId, cancellationToken)
            ?? throw new NotFoundException("El gym no existe.");
        var owner = (await links.ListOwnersAsync(gymId, cancellationToken)).FirstOrDefault(o => o.UserId == userId)
            ?? throw new NotFoundException("No existe esa persona con ese acceso en el gimnasio.");

        var link = await links.CopyLinkAsync(gymId, userId, Domain.Identity.UserRole.Owner, actorId, cancellationToken);

        await emailSender.SendAsync(
            link.RequiresLogin
                ? EmailTemplates.OwnerAccessGranted(owner.Email, owner.FullName, gym.Name, link.Url)
                : EmailTemplates.OwnerInvitation(owner.Email, owner.FullName, gym.Name, link.Url),
            cancellationToken);
        logger.LogInformation("Acceso reenviado al owner {UserId} del gym {GymId} por {ActorId}", userId, gymId, actorId);
    }
}
