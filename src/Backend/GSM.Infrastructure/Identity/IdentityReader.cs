using GSM.Application.Identity.Public;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Identity;

public sealed class IdentityReader(GsmDbContext dbContext) : IIdentityReader
{
    public async Task<IdentityUserDto?> GetUserAsync(
        Guid userId, CancellationToken cancellationToken = default)
        => await dbContext.Users
            .Where(u => u.Id == userId)
            .Select(u => new IdentityUserDto(u.Id, u.FullName, u.Role, u.GymId))
            .FirstOrDefaultAsync(cancellationToken);
}
