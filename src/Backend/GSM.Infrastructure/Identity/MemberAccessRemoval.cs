using GSM.Application.Identity.Abstractions;
using GSM.Domain.Identity;
using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Identity;

public sealed class MemberAccessRemoval(GsmDbContext dbContext, UserManager<AppUser> userManager) : IMemberAccessRemoval
{
    public async Task<MemberAccessRemovalResult> RemoveAsync(
        Guid userId, Guid gymId, CancellationToken cancellationToken = default)
    {
        var accessIds = await dbContext.GymAccesses
            .Where(a => a.UserId == userId && a.GymId == gymId && !a.IsTeam && a.Role == UserRole.Member)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

        if (accessIds.Count > 0)
        {
            await dbContext.RefreshTokens
                .Where(t => t.GymAccessId != null && accessIds.Contains(t.GymAccessId.Value))
                .ExecuteDeleteAsync(cancellationToken);
            await dbContext.GymAccesses
                .Where(a => accessIds.Contains(a.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || user.IsSuperAdmin)
        {
            return new MemberAccessRemovalResult(accessIds.Count > 0, false);
        }

        var remaining = await dbContext.GymAccesses.AnyAsync(a => a.UserId == userId, cancellationToken);
        if (remaining)
        {
            return new MemberAccessRemovalResult(accessIds.Count > 0, false);
        }

        // Sin accesos no queda nada que la cuenta pueda hacer: se elimina (y con ella el email, que queda libre).
        await dbContext.RefreshTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        var deleted = await userManager.DeleteAsync(user);
        return new MemberAccessRemovalResult(accessIds.Count > 0, deleted.Succeeded);
    }
}
