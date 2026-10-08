using GSM.Application.Identity.Abstractions;
using GSM.Domain.Identity;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Identity;

public sealed class GymAccessService(GsmDbContext dbContext) : IGymAccessService
{
    public async Task<IReadOnlyList<GymAccessRecord>> ListActiveAsync(
        Guid userId, CancellationToken cancellationToken = default)
        => await dbContext.GymAccesses
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.IsActive)
            .OrderByDescending(a => a.LastUsedAt.HasValue)
            .ThenByDescending(a => a.LastUsedAt)
            .ThenBy(a => a.CreatedAt)
            .Select(a => new GymAccessRecord(a.Id, a.UserId, a.GymId, a.Role, a.LastUsedAt))
            .ToListAsync(cancellationToken);

    public async Task<GymAccessRecord?> GetActiveAsync(
        Guid userId, Guid accessId, CancellationToken cancellationToken = default)
        => await dbContext.GymAccesses
            .AsNoTracking()
            .Where(a => a.Id == accessId && a.UserId == userId && a.IsActive)
            .Select(a => new GymAccessRecord(a.Id, a.UserId, a.GymId, a.Role, a.LastUsedAt))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<GymAccessRecord?> FindActiveAsync(
        Guid userId, Guid gymId, UserRole role, CancellationToken cancellationToken = default)
    {
        var isTeam = GymAccess.IsTeamRole(role);
        return await dbContext.GymAccesses
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.GymId == gymId && a.IsTeam == isTeam && a.Role == role && a.IsActive)
            .Select(a => new GymAccessRecord(a.Id, a.UserId, a.GymId, a.Role, a.LastUsedAt))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<StaffPermission>> GetEffectivePermissionsAsync(
        GymAccessRecord access, CancellationToken cancellationToken = default)
    {
        switch (access.Role)
        {
            case UserRole.Owner:
                return new HashSet<StaffPermission>(StaffPermissions.All);
            case UserRole.Staff:
                var profile = await dbContext.GymAccesses
                    .AsNoTracking()
                    .Where(a => a.Id == access.Id)
                    .Select(a => a.Profile)
                    .FirstOrDefaultAsync(cancellationToken);
                var adjustments = await dbContext.GymAccessPermissions
                    .AsNoTracking()
                    .Where(p => p.GymAccessId == access.Id)
                    .Select(p => new { p.Permission, p.Granted })
                    .ToListAsync(cancellationToken);

                // Un staff sin perfil (no debería existir tras la migración) no tiene permisos especiales.
                return StaffPermissions.Effective(
                    profile ?? StaffProfile.Reception, adjustments.Select(a => (a.Permission, a.Granted)));
            default:
                return new HashSet<StaffPermission>();
        }
    }

    public async Task MarkUsedAsync(Guid accessId, DateTimeOffset now, CancellationToken cancellationToken = default)
        => await dbContext.GymAccesses
            .Where(a => a.Id == accessId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.LastUsedAt, now), cancellationToken);
}
