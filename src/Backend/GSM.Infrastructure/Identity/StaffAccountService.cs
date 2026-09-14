using GSM.Application.Identity.Abstractions;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Identity;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Identity;

public sealed class StaffAccountService(UserManager<AppUser> userManager, GsmDbContext dbContext)
    : IStaffAccountService
{
    public async Task<IReadOnlyList<StaffAccount>> ListAsync(Guid gymId, CancellationToken cancellationToken = default)
        => await userManager.Users
            .Where(u => u.GymId == gymId && u.Role == UserRole.Staff)
            .OrderByDescending(u => u.IsActive).ThenBy(u => u.FullName)
            .Select(u => new StaffAccount(u.Id, u.Email!, u.FullName, u.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<StaffInvitationTicket> CreateAsync(
        Guid gymId, string email, string fullName, CancellationToken cancellationToken = default)
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
            Role = UserRole.Staff,
            GymId = gymId,
            IsActive = true,
        };

        // Sin contraseña: no puede iniciar sesión hasta que la defina con el código.
        var result = await userManager.CreateAsync(user);
        if (!result.Succeeded)
        {
            throw new ValidationDomainException(
                "No se pudo crear el usuario: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        return new StaffInvitationTicket(ToAccount(user), token);
    }

    public async Task<StaffAccount> SetActiveAsync(
        Guid gymId, Guid userId, bool active, CancellationToken cancellationToken = default)
    {
        var user = await userManager.Users
            .FirstOrDefaultAsync(u => u.Id == userId && u.GymId == gymId && u.Role == UserRole.Staff, cancellationToken)
            ?? throw new NotFoundException("No existe ese usuario de staff en el gym.");

        if (user.IsActive == active)
        {
            return ToAccount(user);
        }

        user.IsActive = active;

        if (!active)
        {
            // El access token vigente caduca solo; sin refresh no hay sesión nueva.
            var now = DateTimeOffset.UtcNow;
            var tokens = await dbContext.RefreshTokens
                .Where(t => t.UserId == userId && t.RevokedAt == null)
                .ToListAsync(cancellationToken);
            foreach (var token in tokens)
            {
                token.Revoke(now);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToAccount(user);
    }

    private static StaffAccount ToAccount(AppUser user) => new(user.Id, user.Email!, user.FullName, user.IsActive);
}
