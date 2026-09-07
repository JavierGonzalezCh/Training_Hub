namespace GSM.Application.Identity.Abstractions;

/// <summary>
/// Ciclo de vida del refresh token opaco. La implementación (Infrastructure) genera el token
/// aleatorio, guarda solo su hash y aplica rotación. El texto plano solo se devuelve una vez,
/// al emitirlo.
/// </summary>
public interface IRefreshTokenService
{
    /// <summary>Emite un refresh token nuevo para el usuario y devuelve su texto plano.</summary>
    Task<string> IssueAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Devuelve el <c>userId</c> dueño del token si está vigente (no expirado, no revocado); si no, <c>null</c>.</summary>
    Task<Guid?> ResolveActiveUserAsync(string plaintextToken, CancellationToken cancellationToken = default);

    /// <summary>Revoca el token. Idempotente: un token inexistente o ya revocado no es error.</summary>
    Task RevokeAsync(string plaintextToken, CancellationToken cancellationToken = default);
}
