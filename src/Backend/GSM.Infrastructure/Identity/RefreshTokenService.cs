using System.Security.Cryptography;
using System.Text;
using GSM.Application.Common.Abstractions;
using GSM.Application.Identity;
using GSM.Application.Identity.Abstractions;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GSM.Infrastructure.Identity;

public sealed class RefreshTokenService(GsmDbContext dbContext, IOptions<JwtOptions> options, IClock clock)
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

    public async Task<string> DescribeInactiveAsync(string plaintextToken, CancellationToken cancellationToken = default)
    {
        var hash = Hash(plaintextToken);
        var token = await dbContext.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        return token is null ? "invalid" : token.RevokedAt is not null ? "revoked" : "expired";
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

    public async Task RevokeAllForUserAsync(
        Guid userId, string? keepPlaintextToken = null, CancellationToken cancellationToken = default)
    {
        var keepHash = string.IsNullOrWhiteSpace(keepPlaintextToken) ? null : Hash(keepPlaintextToken);
        var now = clock.UtcNow;

        var tokens = await dbContext.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null && (keepHash == null || t.TokenHash != keepHash))
            .ToListAsync(cancellationToken);

        foreach (var token in tokens)
        {
            token.Revoke(now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string Hash(string token)
        => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
