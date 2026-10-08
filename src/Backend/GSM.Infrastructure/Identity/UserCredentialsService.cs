using GSM.Application.Identity;
using GSM.Application.Identity.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace GSM.Infrastructure.Identity;

public sealed class UserCredentialsService(UserManager<AppUser> userManager) : IUserCredentialsService
{
    public async Task<AccountIdentity?> ValidateCredentialsAsync(
        string email, string password, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email);

        if (user is not { IsActive: true })
        {
            return null;
        }

        return await userManager.CheckPasswordAsync(user, password) ? Map(user) : null;
    }

    public async Task<AccountIdentity?> FindActiveByIdAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        return user is { IsActive: true } ? Map(user) : null;
    }

    private static AccountIdentity Map(AppUser user)
        => new(user.Id, user.Email ?? string.Empty, user.FullName, user.IsSuperAdmin);
}
