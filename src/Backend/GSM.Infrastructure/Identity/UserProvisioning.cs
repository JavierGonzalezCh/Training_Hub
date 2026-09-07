using GSM.Application.Identity.Public;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace GSM.Infrastructure.Identity;

public sealed class UserProvisioning(UserManager<AppUser> userManager) : IUserProvisioning
{
    public async Task<Guid> CreateGymUserAsync(
        string email,
        string fullName,
        string password,
        UserRole role,
        Guid gymId,
        CancellationToken cancellationToken = default)
    {
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            throw new ConflictException($"Ya existe un usuario con el email '{email}'.");
        }

        var user = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = fullName,
            Role = role,
            GymId = gymId,
            IsActive = true,
        };

        var result = await userManager.CreateAsync(user, password);

        if (!result.Succeeded)
        {
            throw new ValidationDomainException(
                "No se pudo crear el usuario: " +
                string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        return user.Id;
    }
}
