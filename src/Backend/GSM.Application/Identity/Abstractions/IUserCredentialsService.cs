namespace GSM.Application.Identity.Abstractions;

/// <summary>
/// Acceso al store de usuarios de ASP.NET Identity. Implementada en Infrastructure sobre
/// <c>UserManager&lt;AppUser&gt;</c>. Los casos de uso nunca ven la entidad de Identity.
/// </summary>
public interface IUserCredentialsService
{
    /// <summary>Valida email + password. <c>null</c> si no existe, la clave no coincide o el usuario está inactivo.</summary>
    Task<AuthenticatedUser?> ValidateCredentialsAsync(
        string email, string password, CancellationToken cancellationToken = default);

    /// <summary>Usuario activo por id. <c>null</c> si no existe o está inactivo.</summary>
    Task<AuthenticatedUser?> FindActiveByIdAsync(
        Guid userId, CancellationToken cancellationToken = default);
}
