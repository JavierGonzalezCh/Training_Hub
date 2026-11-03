using GSM.Application.Common;
using GSM.Application.Identity.Abstractions;
using GSM.Domain.Common.Exceptions;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Identity;

/// <summary>
/// Cuentas de un gym vistas por el SuperAdmin y "Cambiar clave" (soporte sin correo). La contraseña se fija con
/// un token de restablecimiento interno, igual que la activación: aplica la política y renueva el
/// <c>SecurityStamp</c>, que anula los enlaces de activación y restablecimiento pendientes. Cada cambio queda en
/// la auditoría de accesos (<see cref="StaffAccessAuditAction.PasswordSetByAdmin"/>).
/// </summary>
public sealed class AdminAccountService(
    UserManager<AppUser> userManager,
    GsmDbContext dbContext,
    IRefreshTokenService refreshTokens) : IAdminAccountService
{
    public async Task<PagedResult<GymAccount>> ListAsync(
        Guid gymId, string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query =
            from access in dbContext.GymAccesses
            join user in dbContext.Users on access.UserId equals user.Id
            where access.GymId == gymId
            select new { access, user };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(x =>
                EF.Functions.ILike(x.user.FullName, pattern) || EF.Functions.ILike(x.user.Email!, pattern));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.access.Role)
            .ThenByDescending(x => x.access.IsActive)
            .ThenBy(x => x.user.FullName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new GymAccount(
                x.user.Id, x.user.Email!, x.user.FullName, x.access.Role,
                x.access.IsActive && x.user.IsActive, x.user.PasswordHash != null))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return new PagedResult<GymAccount>(items, page, pageSize, total);
    }

    public async Task SetPasswordAsync(
        Guid gymId, Guid userId, string newPassword, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        // La auditoría cuelga de un acceso: el de equipo si lo tiene, si no el de miembro.
        var access = await dbContext.GymAccesses
                .Where(a => a.UserId == userId && a.GymId == gymId)
                .OrderBy(a => a.Role)
                .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Esa persona no tiene acceso a este gimnasio.");

        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("Esa persona no tiene acceso a este gimnasio.");

        if (user.IsSuperAdmin)
        {
            throw new ForbiddenDomainException("La contraseña del SuperAdmin solo se cambia desde su propia cuenta.");
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded)
        {
            throw new ValidationDomainException(
                "No se pudo cambiar la contraseña: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        // Con una contraseña elegida (o puesta por soporte) ya no queda la clave provisional del primer ingreso.
        if (user.AccessFailedCount != 0 || user.LockoutEnd is not null || user.MustChangePassword)
        {
            user.AccessFailedCount = 0;
            user.LockoutEnd = null;
            user.MustChangePassword = false;
            await userManager.UpdateAsync(user);
        }

        await refreshTokens.RevokeAllForUserAsync(user.Id, keepPlaintextToken: null, cancellationToken);

        dbContext.StaffAccessAudits.Add(StaffAccessAudit.Create(
            gymId, access.Id, userId, actorUserId, StaffAccessAuditAction.PasswordSetByAdmin));
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
