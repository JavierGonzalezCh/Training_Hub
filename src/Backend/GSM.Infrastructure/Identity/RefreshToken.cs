using GSM.Domain.Common;

namespace GSM.Infrastructure.Identity;

/// <summary>
/// Refresh token opaco. Solo se persiste el hash SHA-256 (base64) del valor entregado al cliente.
/// La rotación revoca el token usado y emite otro.
/// </summary>
public sealed class RefreshToken : BaseEntity
{
    private RefreshToken()
    {
    }

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public static RefreshToken Issue(Guid userId, string tokenHash, DateTimeOffset expiresAt) => new()
    {
        UserId = userId,
        TokenHash = tokenHash,
        ExpiresAt = expiresAt,
    };

    public bool IsActiveAt(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public void Revoke(DateTimeOffset now)
    {
        RevokedAt ??= now;
    }
}
