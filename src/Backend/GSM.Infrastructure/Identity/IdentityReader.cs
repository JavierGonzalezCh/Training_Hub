using GSM.Application.Identity.Public;
using GSM.Domain.Identity;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Identity;

public sealed class IdentityReader(GsmDbContext dbContext) : IIdentityReader
{
    /// <summary>
    /// Cómo se muestra al SuperAdmin en el historial de cualquier gym (S6, <c>GSM-modulo-superadmin.md</c> §V6).
    /// Este lector es el único punto por el que otros módulos resuelven el nombre de un autor, así que la
    /// firma "GSM" vive aquí y no repartida por los módulos.
    /// </summary>
    public const string PlatformDisplayName = "GSM";

    public async Task<IdentityUserDto?> GetUserAsync(
        Guid userId, CancellationToken cancellationToken = default)
        => await dbContext.Users
            .Where(u => u.Id == userId)
            .Select(u => new IdentityUserDto(
                u.Id, u.IsSuperAdmin ? PlatformDisplayName : u.FullName, u.IsSuperAdmin))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<TrainerCandidateDto>> ListTrainersAsync(
        Guid gymId, CancellationToken cancellationToken = default)
    {
        var rows = await (
                from access in dbContext.GymAccesses
                join user in dbContext.Users on access.UserId equals user.Id
                where access.GymId == gymId && access.IsTeam && access.IsActive && user.IsActive
                select new { AccessId = access.Id, UserId = user.Id, user.FullName, access.Role, access.Profile })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var staffAccessIds = rows.Where(r => r.Role == UserRole.Staff).Select(r => r.AccessId).ToList();
        var adjustments = (await dbContext.GymAccessPermissions
                .AsNoTracking()
                .Where(p => staffAccessIds.Contains(p.GymAccessId) && p.Permission == StaffPermission.Trainer)
                .Select(p => new { p.GymAccessId, p.Granted })
                .ToListAsync(cancellationToken))
            .ToDictionary(p => p.GymAccessId, p => p.Granted);

        return rows
            .Where(r => r.Role == UserRole.Owner
                || HasTrainer(r.Profile, adjustments.TryGetValue(r.AccessId, out var granted) ? granted : null))
            .OrderBy(r => r.FullName)
            .Select(r => new TrainerCandidateDto(r.UserId, r.FullName, r.Role == UserRole.Owner))
            .ToList();
    }

    public async Task<bool> CanBeTrainerAsync(Guid userId, Guid gymId, CancellationToken cancellationToken = default)
    {
        var access = await (
                from a in dbContext.GymAccesses
                join user in dbContext.Users on a.UserId equals user.Id
                where a.UserId == userId && a.GymId == gymId && a.IsTeam && a.IsActive && user.IsActive
                select new { a.Id, a.Role, a.Profile })
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
        if (access is null)
        {
            return false;
        }

        if (access.Role == UserRole.Owner)
        {
            return true;
        }

        var adjustment = await dbContext.GymAccessPermissions
            .AsNoTracking()
            .Where(p => p.GymAccessId == access.Id && p.Permission == StaffPermission.Trainer)
            .Select(p => (bool?)p.Granted)
            .FirstOrDefaultAsync(cancellationToken);
        return HasTrainer(access.Profile, adjustment);
    }

    /// <summary>Permiso efectivo = el del perfil, salvo que un ajuste lo agregue (<c>true</c>) o lo quite (<c>false</c>).</summary>
    private static bool HasTrainer(StaffProfile? profile, bool? adjustment)
        => adjustment
           ?? StaffPermissions.ForProfile(profile ?? StaffProfile.Reception).Contains(StaffPermission.Trainer);
}
