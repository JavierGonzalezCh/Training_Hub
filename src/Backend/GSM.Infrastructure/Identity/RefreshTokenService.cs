using System.Security.Cryptography;
using System.Text;
using GSM.Application.Identity;
using GSM.Application.Identity.Abstractions;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GSM.Infrastructure.Identity;

public sealed class RefreshTokenService(GsmDbContext dbContext, IOptions<JwtOptions> options)
    : IRefreshTokenService
{
    private readonly int _lifetimeDays = options.Value.RefreshTokenDays;

    public async Task<string> IssueAsync(
        Guid userId, Guid? gymAccessId, CancellationToken cancellationToken = default)
    {
        var plaintext = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var now = DateTimeOffset.UtcNow;

        dbContext.RefreshTokens.Add(
            RefreshToken.Issue(userId, Hash(plaintext), now.AddDays(_lifetimeDays), gymAccessId));

        await dbContext.SaveChangesAsync(cancellationToken);
        return plaintext;
    }

    public async Task<RefreshTokenOwner?> ResolveActiveAsync(
        string plaintextToken, CancellationToken cancellationToken = default)
    {
        var hash = Hash(plaintextToken);
        var token = await dbContext.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        return token is not null && token.IsActiveAt(DateTimeOffset.UtcNow)
            ? new RefreshTokenOwner(token.UserId, token.GymAccessId)
            : null;
    }

    public async Task RevokeAsync(string plaintextToken, CancellationToken cancellationToken = default)
    {
        var hash = Hash(plaintextToken);
        var token = await dbContext.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (token is null)
        {
            return;
        }

        token.Revoke(DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeOwnedAsync(
        string plaintextToken, Guid userId, CancellationToken cancellationToken = default)
    {
        var hash = Hash(plaintextToken);
        var token = await dbContext.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.UserId == userId, cancellationToken);

        if (token is null)
        {
            return;
        }

        token.Revoke(DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string Hash(string token)
        => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
