using GSM.Application.Identity.Public;
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
}
