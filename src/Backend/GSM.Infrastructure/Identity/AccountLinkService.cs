using GSM.Application.Common.Abstractions;
using GSM.Application.Identity.Abstractions;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Identity;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Identity;

/// <summary>
/// "Copiar enlace" de staff y owners (§4). Los códigos no se guardan —se firman con el usuario y su
/// <c>SecurityStamp</c>—, así que "el enlace vigente" no se puede recuperar: cada copia emite uno nuevo de 72 h
/// y anula los anteriores (renueva el <c>SecurityStamp</c>, decisión P19a). Cada copia queda en la
/// auditoría de accesos (<see cref="StaffAccessAuditAction.LinkCopied"/>): quién, a quién, cuándo.
/// </summary>
public sealed class AccountLinkService(
    UserManager<AppUser> userManager,
    GsmDbContext dbContext,
    IAccountLinkBuilder links,
    IClock clock) : IAccountLinkService
{
    public async Task<IReadOnlyList<OwnerAccount>> ListOwnersAsync(
        Guid gymId, CancellationToken cancellationToken = default)
        => await (
                from access in dbContext.GymAccesses
                join user in dbContext.Users on access.UserId equals user.Id
                where access.GymId == gymId && access.Role == UserRole.Owner
                orderby access.IsActive descending, user.FullName
                select new OwnerAccount(
                    user.Id, user.Email!, user.FullName, access.IsActive && user.IsActive, user.PasswordHash != null))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public async Task<AccountLink> CopyLinkAsync(
        Guid gymId, Guid userId, UserRole role, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (role is not (UserRole.Staff or UserRole.Owner))
        {
            throw new ArgumentException("Solo el staff y los owners reciben enlaces de activación.", nameof(role));
        }

        var access = await dbContext.GymAccesses
                .FirstOrDefaultAsync(a => a.UserId == userId && a.GymId == gymId && a.Role == role, cancellationToken)
            ?? throw new NotFoundException("No existe esa persona con ese acceso en el gimnasio.");

        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("No existe esa persona con ese acceso en el gimnasio.");

        if (!access.IsActive || !user.IsActive)
        {
            throw new ConflictException("El acceso está desactivado: reactívalo antes de copiar el enlace.");
        }

        AccountLink link;
        if (await userManager.HasPasswordAsync(user))
        {
            // Ya activó su cuenta: el enlace la lleva a entrar, no a crear una contraseña.
            link = new AccountLink(links.ActivationUrl(user.Email!, null), RequiresLogin: true, ExpiresAt: null);
        }
        else
        {
            // P19a: renovar el SecurityStamp invalida los enlaces anteriores; siempre vale el último.
            await userManager.UpdateSecurityStampAsync(user);
            var code = await userManager.GenerateUserTokenAsync(
                user, ActivationTokenProvider.ProviderName, ActivationTokenProvider.Purpose);
            link = new AccountLink(
                links.ActivationUrl(user.Email!, code),
                RequiresLogin: false,
                ExpiresAt: clock.UtcNow + ActivationTokenProvider.Validity);
        }

        dbContext.StaffAccessAudits.Add(StaffAccessAudit.Create(
            gymId, access.Id, userId, actorUserId, StaffAccessAuditAction.LinkCopied,
            newValue: link.RequiresLogin ? "login" : "activation"));
        await dbContext.SaveChangesAsync(cancellationToken);

        return link;
    }
}
