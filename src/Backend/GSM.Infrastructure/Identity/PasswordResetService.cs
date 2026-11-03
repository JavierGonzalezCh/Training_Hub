using GSM.Application.Identity;
using GSM.Application.Identity.Abstractions;
using GSM.Domain.Common.Exceptions;
using Microsoft.AspNetCore.Identity;

namespace GSM.Infrastructure.Identity;

public sealed class PasswordResetService(UserManager<AppUser> userManager, IRefreshTokenService refreshTokens)
    : IPasswordResetService
{
    private const string InvalidLink = "El enlace no es válido o venció.";

    public async Task<PasswordResetTicket?> CreateTokenAsync(
        string email, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is not { IsActive: true })
        {
            return null;
        }

        // Proveedor "GsmReset": vence a las 24 horas (ResetTokenProvider).
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        return new PasswordResetTicket(user.Email!, user.FullName, token);
    }

    public async Task ResetAsync(
        string email, string token, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is not { IsActive: true })
        {
            throw new ValidationDomainException(InvalidLink);
        }

        var result = await userManager.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded)
        {
            throw new ValidationDomainException(
                "No se pudo restablecer la contraseña: " +
                string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        await AfterPasswordSetAsync(user, cancellationToken);
    }

    public async Task<AccountIdentity> ActivateAsync(
        string email, string code, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email);

        // Un enlace de activación nunca pisa una contraseña existente ni activa al SuperAdmin.
        if (user is not { IsActive: true, IsSuperAdmin: false }
            || await userManager.HasPasswordAsync(user)
            || !await userManager.VerifyUserTokenAsync(
                user, ActivationTokenProvider.ProviderName, ActivationTokenProvider.Purpose, code))
        {
            throw new ValidationDomainException(InvalidLink);
        }

        // Se fija la contraseña con un token de restablecimiento interno: aplica la política de contraseñas
        // y cambia el SecurityStamp, que invalida el código de activación (un solo uso).
        var internalToken = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, internalToken, newPassword);
        if (!result.Succeeded)
        {
            throw new ValidationDomainException(
                "No se pudo definir la contraseña: " +
                string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        await AfterPasswordSetAsync(user, cancellationToken);
        return new AccountIdentity(user.Id, user.Email ?? string.Empty, user.FullName, user.IsSuperAdmin, user.MustChangePassword);
    }

    // Una contraseña nueva por enlace prueba el acceso al correo: levanta el bloqueo por intentos y cierra
    // todas las sesiones de la persona (en todos sus gyms).
    private async Task AfterPasswordSetAsync(AppUser user, CancellationToken cancellationToken)
    {
        // Con una contraseña elegida (o puesta por soporte) ya no queda la clave provisional del primer ingreso.
        if (user.AccessFailedCount != 0 || user.LockoutEnd is not null || user.MustChangePassword)
        {
            user.AccessFailedCount = 0;
            user.LockoutEnd = null;
            user.MustChangePassword = false;
            await userManager.UpdateAsync(user);
        }

        await refreshTokens.RevokeAllForUserAsync(user.Id, keepPlaintextToken: null, cancellationToken);
    }
}
