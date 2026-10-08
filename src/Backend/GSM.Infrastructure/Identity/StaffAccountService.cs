using GSM.Application.Common.Abstractions;
using GSM.Application.Identity;
using GSM.Application.Identity.Abstractions;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Identity;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Identity;

public sealed class StaffAccountService(UserManager<AppUser> userManager, GsmDbContext dbContext, IClock clock)
    : IStaffAccountService
{
    public async Task<IReadOnlyList<StaffAccount>> ListAsync(Guid gymId, CancellationToken cancellationToken = default)
    {
        var rows = await (
                from access in dbContext.GymAccesses
                join user in dbContext.Users on access.UserId equals user.Id
                where access.GymId == gymId && access.Role == UserRole.Staff
                orderby access.IsActive descending, user.FullName
                select new { access.Id, UserId = user.Id, user.Email, user.FullName, access.IsActive, access.Profile })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var accessIds = rows.Select(r => r.Id).ToList();
        var adjustments = (await dbContext.GymAccessPermissions
                .AsNoTracking()
                .Where(p => accessIds.Contains(p.GymAccessId))
                .Select(p => new { p.GymAccessId, p.Permission, p.Granted })
                .ToListAsync(cancellationToken))
            .ToLookup(p => p.GymAccessId);

        return rows
            .Select(r => ToAccount(
                r.UserId,
                r.Email!,
                r.FullName,
                r.IsActive,
                r.Profile ?? StaffProfile.Reception,
                adjustments[r.Id].Select(a => (a.Permission, a.Granted)).ToList()))
            .ToList();
    }

    public async Task<StaffInvitationTicket> CreateAsync(
        Guid gymId,
        string email,
        string fullName,
        StaffProfile profile,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
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

            await AddAccessAsync(user, gymId, profile, actorUserId, cancellationToken);

            return new StaffInvitationTicket(ToAccount(user, true, profile), Token: null);
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

        await AddAccessAsync(user, gymId, profile, actorUserId, cancellationToken);

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        return new StaffInvitationTicket(ToAccount(user, true, profile), token);
    }

    public async Task<StaffAccount> SetActiveAsync(
        Guid gymId, Guid userId, bool active, CancellationToken cancellationToken = default)
    {
        var access = await FindStaffAccessAsync(gymId, userId, cancellationToken);
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("No existe ese usuario de staff en el gym.");

        if (access.IsActive == active)
        {
            return ToAccount(user, access, await LoadAdjustmentsAsync(access.Id, cancellationToken));
        }

        if (active)
        {
            access.Reactivate();
        }
        else
        {
            access.Deactivate(clock.UtcNow);

            // Solo las sesiones de este gym: el access token vigente caduca solo y sin refresh no hay
            // sesión nueva. Sus otros gyms quedan intactos.
            await RevokeSessionsAsync(access.Id, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToAccount(user, access, await LoadAdjustmentsAsync(access.Id, cancellationToken));
    }

    public async Task<StaffAccount> UpdateAccessAsync(
        Guid gymId,
        Guid userId,
        StaffProfile profile,
        IReadOnlyCollection<StaffPermission> permissions,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var access = await FindStaffAccessAsync(gymId, userId, cancellationToken);
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("No existe ese usuario de staff en el gym.");

        var oldProfile = access.Profile ?? StaffProfile.Reception;
        var existing = await dbContext.GymAccessPermissions
            .Where(p => p.GymAccessId == access.Id)
            .ToListAsync(cancellationToken);

        var oldEffective = StaffPermissions.Effective(oldProfile, existing.Select(p => (p.Permission, p.Granted)));
        var desired = permissions.ToHashSet();
        var baseline = StaffPermissions.ForProfile(profile);

        // Ajustes nuevos = lo que difiere del perfil.
        var wanted = StaffPermissions.All
            .Where(p => baseline.Contains(p) != desired.Contains(p))
            .ToDictionary(p => p, p => desired.Contains(p));

        foreach (var row in existing)
        {
            if (!wanted.TryGetValue(row.Permission, out var granted))
            {
                dbContext.GymAccessPermissions.Remove(row);
            }
            else if (row.Granted != granted)
            {
                row.SetGranted(granted);
            }
        }

        foreach (var (permission, granted) in wanted)
        {
            if (existing.All(r => r.Permission != permission))
            {
                dbContext.GymAccessPermissions.Add(GymAccessPermission.Create(access.Id, permission, granted));
            }
        }

        var profileChanged = oldProfile != profile;
        if (profileChanged)
        {
            access.ChangeProfile(profile);
            Audit(access, actorUserId, StaffAccessAuditAction.ProfileChanged,
                oldValue: PermissionName.ToProfileName(oldProfile), newValue: PermissionName.ToProfileName(profile));
        }

        var added = desired.Except(oldEffective).OrderBy(p => p).ToList();
        var removed = oldEffective.Except(desired).OrderBy(p => p).ToList();
        foreach (var permission in added)
        {
            Audit(access, actorUserId, StaffAccessAuditAction.PermissionGranted, permission);
        }

        foreach (var permission in removed)
        {
            Audit(access, actorUserId, StaffAccessAuditAction.PermissionRevoked, permission);
        }

        // Quitar un permiso (o pasar a un perfil con menos) cierra las sesiones de esa persona en este
        // gym (§5.3). Agregar no: aplica en la próxima renovación del token.
        if (removed.Count > 0)
        {
            await RevokeSessionsAsync(access.Id, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return ToAccount(user, access, await LoadAdjustmentsAsync(access.Id, cancellationToken));
    }

    public async Task<IReadOnlyList<StaffAuditEntry>> ListAuditAsync(
        Guid gymId, Guid? userId, int take, CancellationToken cancellationToken = default)
        => await (
                from audit in dbContext.StaffAccessAudits
                join target in dbContext.Users on audit.TargetUserId equals target.Id
                join actor in dbContext.Users on audit.ActorUserId equals actor.Id
                where audit.GymId == gymId && (userId == null || audit.TargetUserId == userId)
                orderby audit.CreatedAt descending, audit.Id descending
                select new StaffAuditEntry(
                    audit.Id,
                    audit.TargetUserId,
                    target.FullName,
                    audit.ActorUserId,
                    actor.FullName,
                    audit.Action.ToString(),
                    audit.Permission,
                    audit.OldValue,
                    audit.NewValue,
                    audit.CreatedAt))
            .AsNoTracking()
            .Take(take)
            .ToListAsync(cancellationToken);

    private async Task AddAccessAsync(
        AppUser user, Guid gymId, StaffProfile profile, Guid actorUserId, CancellationToken cancellationToken)
    {
        var access = GymAccess.Grant(user.Id, gymId, UserRole.Staff, profile);
        dbContext.GymAccesses.Add(access);
        Audit(access, actorUserId, StaffAccessAuditAction.Invited, newValue: PermissionName.ToProfileName(profile));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private void Audit(
        GymAccess access,
        Guid actorUserId,
        StaffAccessAuditAction action,
        StaffPermission? permission = null,
        string? oldValue = null,
        string? newValue = null)
        => dbContext.StaffAccessAudits.Add(StaffAccessAudit.Create(
            access.GymId, access.Id, access.UserId, actorUserId, action, permission, oldValue, newValue));

    private async Task<GymAccess> FindStaffAccessAsync(Guid gymId, Guid userId, CancellationToken cancellationToken)
        => await dbContext.GymAccesses
               .FirstOrDefaultAsync(
                   a => a.UserId == userId && a.GymId == gymId && a.Role == UserRole.Staff, cancellationToken)
           ?? throw new NotFoundException("No existe ese usuario de staff en el gym.");

    private async Task<List<(StaffPermission Permission, bool Granted)>> LoadAdjustmentsAsync(
        Guid accessId, CancellationToken cancellationToken)
        => (await dbContext.GymAccessPermissions
                .AsNoTracking()
                .Where(p => p.GymAccessId == accessId)
                .Select(p => new { p.Permission, p.Granted })
                .ToListAsync(cancellationToken))
            .Select(p => (p.Permission, p.Granted))
            .ToList();

    private async Task RevokeSessionsAsync(Guid accessId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var tokens = await dbContext.RefreshTokens
            .Where(t => t.GymAccessId == accessId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var token in tokens)
        {
            token.Revoke(now);
        }
    }

    private static StaffAccount ToAccount(AppUser user, GymAccess access, List<(StaffPermission, bool)> adjustments)
        => ToAccount(
            user.Id, user.Email!, user.FullName, access.IsActive,
            access.Profile ?? StaffProfile.Reception, adjustments);

    private static StaffAccount ToAccount(AppUser user, bool isActive, StaffProfile profile)
        => ToAccount(user.Id, user.Email!, user.FullName, isActive, profile, []);

    private static StaffAccount ToAccount(
        Guid id,
        string email,
        string fullName,
        bool isActive,
        StaffProfile profile,
        List<(StaffPermission Permission, bool Granted)> adjustments)
        => new(
            id,
            email,
            fullName,
            isActive,
            profile,
            IsAdjusted: adjustments.Count > 0,
            StaffPermissions.Effective(profile, adjustments).OrderBy(p => p).ToList());
}
