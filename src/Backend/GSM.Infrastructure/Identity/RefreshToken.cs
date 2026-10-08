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

    /// <summary>
    /// Acceso (<see cref="GymAccess"/>) para el que se emitió la sesión: al renovar se vuelve a emitir
    /// para el mismo gym y rol. <c>null</c> solo para el SuperAdmin.
    /// </summary>
    public Guid? GymAccessId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public static RefreshToken Issue(
        Guid userId, string tokenHash, DateTimeOffset expiresAt, Guid? gymAccessId = null) => new()
    {
        UserId = userId,
        GymAccessId = gymAccessId,
        TokenHash = tokenHash,
        ExpiresAt = expiresAt,
    };

    public bool IsActiveAt(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public void Revoke(DateTimeOffset now)
    {
        RevokedAt ??= now;
    }
}
