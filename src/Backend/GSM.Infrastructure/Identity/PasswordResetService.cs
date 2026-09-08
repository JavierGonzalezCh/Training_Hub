using GSM.Application.Identity.Abstractions;
using GSM.Domain.Common.Exceptions;
using Microsoft.AspNetCore.Identity;

namespace GSM.Infrastructure.Identity;

public sealed class PasswordResetService(UserManager<AppUser> userManager) : IPasswordResetService
{
    public async Task<PasswordResetTicket?> CreateTokenAsync(
        string email, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is not { IsActive: true })
        {
            return null;
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        return new PasswordResetTicket(user.Email!, user.FullName, token);
    }

    public async Task ResetAsync(
        string email, string token, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is not { IsActive: true })
        {
            throw new ValidationDomainException("Solicitud de restablecimiento no válida.");
        }

        var result = await userManager.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded)
        {
            throw new ValidationDomainException(
                "No se pudo restablecer la contraseña: " +
                string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }
}
