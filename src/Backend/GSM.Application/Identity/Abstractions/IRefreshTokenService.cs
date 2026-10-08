namespace GSM.Application.Identity.Abstractions;

/// <summary>
/// Ciclo de vida del refresh token opaco. La implementación (Infrastructure) genera el token
/// aleatorio, guarda solo su hash y aplica rotación. El texto plano solo se devuelve una vez,
/// al emitirlo. Cada token recuerda el acceso (gym + rol) para el que se emitió.
/// </summary>
public interface IRefreshTokenService
{
    /// <summary>
    /// Emite un refresh token nuevo y devuelve su texto plano. <paramref name="gymAccessId"/> es el
    /// acceso de la sesión; <c>null</c> solo para el SuperAdmin.
    /// </summary>
    Task<string> IssueAsync(Guid userId, Guid? gymAccessId, CancellationToken cancellationToken = default);

    /// <summary>Dueño y acceso del token si está vigente (no expirado, no revocado); si no, <c>null</c>.</summary>
    Task<RefreshTokenOwner?> ResolveActiveAsync(string plaintextToken, CancellationToken cancellationToken = default);

    /// <summary>Revoca el token. Idempotente: un token inexistente o ya revocado no es error.</summary>
    Task RevokeAsync(string plaintextToken, CancellationToken cancellationToken = default);

    /// <summary>Revoca el token solo si pertenece a esa persona (para cambiar de gym sin tocar sesiones ajenas).</summary>
    Task RevokeOwnedAsync(string plaintextToken, Guid userId, CancellationToken cancellationToken = default);
}

public sealed record RefreshTokenOwner(Guid UserId, Guid? GymAccessId);
