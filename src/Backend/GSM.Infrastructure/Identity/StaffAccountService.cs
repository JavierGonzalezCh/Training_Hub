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
        => await (
                from access in dbContext.GymAccesses
                join user in dbContext.Users on access.UserId equals user.Id
                where access.GymId == gymId && access.Role == UserRole.Staff
                orderby access.IsActive descending, user.FullName
                select new StaffAccount(user.Id, user.Email!, user.FullName, access.IsActive))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public async Task<StaffInvitationTicket> CreateAsync(
        Guid gymId, string email, string fullName, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email);

        // Cuenta existente: solo se agrega el acceso; su contraseña y su nombre no se tocan.
        if (user is not null)
        {
            if (user.IsSuperAdmin || !user.IsActive)
            {
                throw new ConflictException($"El email '{email}' no puede recibir acceso a un gimnasio.");
            }

            await GymAccessRules.EnsureNoAccessOfSameKindAsync(
                dbContext, user.Id, gymId, UserRole.Staff, cancellationToken);

            dbContext.GymAccesses.Add(GymAccess.Grant(user.Id, gymId, UserRole.Staff));
            await dbContext.SaveChangesAsync(cancellationToken);

            return new StaffInvitationTicket(ToAccount(user, isActive: true), Token: null);
        }

        user = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = fullName,
            IsActive = true,
        };

        // Sin contraseña: no puede iniciar sesión hasta que la defina con el código.
        var result = await userManager.CreateAsync(user);
        if (!result.Succeeded)
        {
            throw new ValidationDomainException(
                "No se pudo crear el usuario: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        dbContext.GymAccesses.Add(GymAccess.Grant(user.Id, gymId, UserRole.Staff));
        await dbContext.SaveChangesAsync(cancellationToken);

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        return new StaffInvitationTicket(ToAccount(user, isActive: true), token);
    }

    public async Task<StaffAccount> SetActiveAsync(
        Guid gymId, Guid userId, bool active, CancellationToken cancellationToken = default)
    {
        var access = await dbContext.GymAccesses
            .FirstOrDefaultAsync(
                a => a.UserId == userId && a.GymId == gymId && a.Role == UserRole.Staff, cancellationToken)
            ?? throw new NotFoundException("No existe ese usuario de staff en el gym.");

        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("No existe ese usuario de staff en el gym.");

        if (access.IsActive == active)
        {
            return ToAccount(user, access.IsActive);
        }

        if (active)
        {
            access.Reactivate();
        }
        else
        {
            var now = DateTimeOffset.UtcNow;
            access.Deactivate(now);

            // Solo las sesiones de este gym: el access token vigente caduca solo y sin refresh no hay
            // sesión nueva. Sus otros gyms siguen intactos.
            var tokens = await dbContext.RefreshTokens
                .Where(t => t.GymAccessId == access.Id && t.RevokedAt == null)
                .ToListAsync(cancellationToken);
            foreach (var token in tokens)
            {
                token.Revoke(now);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToAccount(user, access.IsActive);
    }

    private static StaffAccount ToAccount(AppUser user, bool isActive)
        => new(user.Id, user.Email!, user.FullName, isActive);
}
