using GSM.Application.Common.Abstractions;
using GSM.Application.Identity.Abstractions;
using GSM.Application.Identity.Public;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Identity;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Identity;

/// <summary>
/// Owners de un gym (I4). La regla "siempre queda al menos uno" se decide bajo un candado de Postgres por gym
/// (<c>pg_advisory_xact_lock</c>): dos Owners que se quitan a la vez no pueden dejar el gym sin ninguno.
/// </summary>
public sealed class OwnerAccountService(
    UserManager<AppUser> userManager,
    GsmDbContext dbContext,
    IUserProvisioning provisioning,
    IAccountLinkBuilder links,
    IClock clock) : IOwnerAccountService
{
    public async Task<OwnerInvitationResult> InviteAsync(
        Guid gymId, string email, string fullName, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var existing = await userManager.FindByEmailAsync(email);

        if (existing is not null)
        {
            var teamAccess = await dbContext.GymAccesses
                .FirstOrDefaultAsync(a => a.UserId == existing.Id && a.GymId == gymId && a.IsTeam, cancellationToken);

            if (teamAccess is { Role: UserRole.Staff })
            {
                throw new ConflictException("Esa persona ya es staff del gimnasio: asciéndela a Owner desde el equipo.");
            }

            if (teamAccess is { Role: UserRole.Owner, IsActive: true } && existing.IsActive)
            {
                throw new ConflictException("Esa persona ya es Owner del gimnasio.");
            }

            // Ex-Owner con el acceso desactivado: se reactiva en vez de crear otro.
            if (teamAccess is { Role: UserRole.Owner })
            {
                if (!existing.IsActive)
                {
                    throw new ConflictException($"El email '{email}' no puede recibir acceso a un gimnasio.");
                }

                teamAccess.Reactivate();
                Audit(teamAccess, actorUserId, StaffAccessAuditAction.OwnerAdded);
                await dbContext.SaveChangesAsync(cancellationToken);

                var hasPassword = await userManager.HasPasswordAsync(existing);
                string url;
                if (hasPassword)
                {
                    url = links.ActivationUrl(existing.Email!, null);
                }
                else
                {
                    await userManager.UpdateSecurityStampAsync(existing);
                    var code = await userManager.GenerateUserTokenAsync(
                        existing, ActivationTokenProvider.ProviderName, ActivationTokenProvider.Purpose);
                    url = links.ActivationUrl(existing.Email!, code);
                }

                return new OwnerInvitationResult(ToAccount(existing, teamAccess, hasPassword), url, hasPassword);
            }
        }

        var invitation = await provisioning.InviteGymOwnerAsync(email, fullName, gymId, cancellationToken);

        var access = await dbContext.GymAccesses.FirstAsync(
            a => a.UserId == invitation.UserId && a.GymId == gymId && a.Role == UserRole.Owner, cancellationToken);
        var user = await userManager.FindByIdAsync(invitation.UserId.ToString())
            ?? throw new NotFoundException("No existe esa persona.");

        Audit(access, actorUserId, StaffAccessAuditAction.OwnerAdded);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new OwnerInvitationResult(
            ToAccount(user, access, await userManager.HasPasswordAsync(user)), invitation.ActivationUrl, invitation.HadAccount);
    }

    public async Task<OwnerAccount> PromoteStaffAsync(
        Guid gymId, Guid userId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var access = await dbContext.GymAccesses.FirstOrDefaultAsync(
                a => a.UserId == userId && a.GymId == gymId && a.Role == UserRole.Staff, cancellationToken)
            ?? throw new NotFoundException("No existe ese usuario de staff en el gym.");
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("No existe ese usuario de staff en el gym.");

        if (!access.IsActive || !user.IsActive)
        {
            throw new ConflictException("El staff está desactivado: reactívalo antes de ascenderlo a Owner.");
        }

        var oldProfile = access.Profile ?? StaffProfile.Reception;

        // El Owner tiene todos los permisos: los ajustes sueltos del staff dejan de significar algo.
        var adjustments = await dbContext.GymAccessPermissions
            .Where(p => p.GymAccessId == access.Id)
            .ToListAsync(cancellationToken);
        dbContext.GymAccessPermissions.RemoveRange(adjustments);

        access.PromoteToOwner();
        Audit(access, actorUserId, StaffAccessAuditAction.OwnerPromoted, oldValue: ProfileName(oldProfile));
        await dbContext.SaveChangesAsync(cancellationToken);

        // Sin revocar sesiones: al renovar el token, el acceso ya es de Owner y trae todos los permisos.
        return ToAccount(user, access, await userManager.HasPasswordAsync(user));
    }

    public async Task<OwnerAccount> RemoveAsync(
        Guid gymId,
        Guid userId,
        bool keepAsStaff,
        StaffProfile staffProfile,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Candado por gym hasta el commit: serializa las bajas de Owners del mismo gym.
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({gymId.ToString()}, 0))", cancellationToken);

        var access = await dbContext.GymAccesses.FirstOrDefaultAsync(
                a => a.UserId == userId && a.GymId == gymId && a.Role == UserRole.Owner, cancellationToken)
            ?? throw new NotFoundException("No existe ese Owner en el gimnasio.");
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("No existe ese Owner en el gimnasio.");

        if (!access.IsActive)
        {
            return ToAccount(user, access, await userManager.HasPasswordAsync(user));
        }

        var otherActiveOwners = await (
                from a in dbContext.GymAccesses
                join u in dbContext.Users on a.UserId equals u.Id
                where a.GymId == gymId && a.Role == UserRole.Owner && a.IsActive && u.IsActive && a.UserId != userId
                select a.Id)
            .CountAsync(cancellationToken);

        if (otherActiveOwners == 0)
        {
            throw new ConflictException(
                "No se puede quitar al último Owner: el gimnasio siempre debe tener al menos uno. Agrega a otro primero.");
        }

        if (keepAsStaff)
        {
            access.DemoteToStaff(staffProfile);
            Audit(access, actorUserId, StaffAccessAuditAction.OwnerDemoted, newValue: ProfileName(staffProfile));
        }
        else
        {
            access.Deactivate(clock.UtcNow);
            Audit(access, actorUserId, StaffAccessAuditAction.OwnerRemoved);
        }

        // Sus sesiones en este gym se cierran (las de otros gyms quedan).
        var now = clock.UtcNow;
        var tokens = await dbContext.RefreshTokens
            .Where(t => t.GymAccessId == access.Id && t.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var token in tokens)
        {
            token.Revoke(now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToAccount(user, access, await userManager.HasPasswordAsync(user));
    }

    private void Audit(
        GymAccess access,
        Guid actorUserId,
        StaffAccessAuditAction action,
        string? oldValue = null,
        string? newValue = null)
        => dbContext.StaffAccessAudits.Add(StaffAccessAudit.Create(
            access.GymId, access.Id, access.UserId, actorUserId, action, permission: null, oldValue, newValue));

    private static string ProfileName(StaffProfile profile) => GSM.Application.Identity.PermissionName.ToProfileName(profile);

    private static OwnerAccount ToAccount(AppUser user, GymAccess access, bool hasPassword)
        => new(user.Id, user.Email!, user.FullName, access.IsActive && user.IsActive, hasPassword);
}
