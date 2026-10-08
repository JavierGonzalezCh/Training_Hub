using GSM.Application.Common.Abstractions;
using GSM.Application.Identity;
using GSM.Application.Identity.Abstractions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace GSM.Infrastructure.Identity;

/// <summary>
/// Credenciales sobre ASP.NET Identity. El bloqueo por intentos usa las columnas y la configuración de
/// lockout de Identity (<c>AccessFailedCount</c>, <c>LockoutEnd</c>, <c>Lockout.MaxFailedAccessAttempts</c> y
/// <c>Lockout.DefaultLockoutTimeSpan</c>), pero el instante lo da <see cref="IClock"/>: el
/// <c>UserManager</c> usa <c>DateTimeOffset.UtcNow</c> sin poder inyectarse, y así el bloqueo de 15 minutos
/// se puede probar sin esperar.
/// </summary>
public sealed class UserCredentialsService(
    UserManager<AppUser> userManager,
    IClock clock,
    IOptions<IdentityOptions> identityOptions) : IUserCredentialsService
{
    private LockoutOptions Lockout => identityOptions.Value.Lockout;

    public async Task<CredentialCheck> SignInAsync(
        string email, string password, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email);

        if (user is not { IsActive: true })
        {
            return new CredentialCheck(null);
        }

        var now = clock.UtcNow;

        // Bloqueada: ni se prueba la contraseña ni suma intentos (la buena tampoco entra hasta que venza).
        if (user.LockoutEnd is { } until && until > now)
        {
            return new CredentialCheck(null);
        }

        if (await userManager.CheckPasswordAsync(user, password))
        {
            await ClearFailuresAsync(user);
            return new CredentialCheck(Map(user));
        }

        var lockedNow = await RegisterFailureAsync(user, now);
        return lockedNow ? new CredentialCheck(null, true, Map(user)) : new CredentialCheck(null);
    }

    public async Task<AccountIdentity?> ValidateCredentialsAsync(
        string email, string password, CancellationToken cancellationToken = default)
        => (await SignInAsync(email, password, cancellationToken)).Account;

    public async Task<AccountIdentity?> FindActiveByIdAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        return user is { IsActive: true } ? Map(user) : null;
    }

    public async Task<PasswordChangeOutcome> ChangePasswordAsync(
        Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is not { IsActive: true })
        {
            return new PasswordChangeOutcome(false, Error: "La cuenta no está disponible.");
        }

        var now = clock.UtcNow;
        if (user.LockoutEnd is { } until && until > now)
        {
            return new PasswordChangeOutcome(false, WrongCurrentPassword: true);
        }

        if (!await userManager.CheckPasswordAsync(user, currentPassword))
        {
            var lockedNow = await RegisterFailureAsync(user, now);
            return new PasswordChangeOutcome(false, WrongCurrentPassword: true, LockedOutNow: lockedNow);
        }

        var result = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded)
        {
            return new PasswordChangeOutcome(
                false, Error: "No se pudo cambiar la contraseña: " +
                string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        await ClearFailuresAsync(user);
        return new PasswordChangeOutcome(true);
    }

    /// <summary>Suma un intento fallido; al llegar al máximo bloquea la cuenta. <c>true</c> solo en el intento que bloquea.</summary>
    private async Task<bool> RegisterFailureAsync(AppUser user, DateTimeOffset now)
    {
        user.AccessFailedCount++;

        var lockedNow = user.AccessFailedCount >= Lockout.MaxFailedAccessAttempts;
        if (lockedNow)
        {
            user.LockoutEnd = now + Lockout.DefaultLockoutTimeSpan;
            user.AccessFailedCount = 0;
        }

        await userManager.UpdateAsync(user);
        return lockedNow;
    }

    private async Task ClearFailuresAsync(AppUser user)
    {
        if (user.AccessFailedCount == 0 && user.LockoutEnd is null)
        {
            return;
        }

        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        await userManager.UpdateAsync(user);
    }

    private static AccountIdentity Map(AppUser user)
        => new(user.Id, user.Email ?? string.Empty, user.FullName, user.IsSuperAdmin);
}
