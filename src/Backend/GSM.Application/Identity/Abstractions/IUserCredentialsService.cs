namespace GSM.Application.Identity.Abstractions;

/// <summary>
/// Acceso al store de usuarios de ASP.NET Identity. Implementada en Infrastructure sobre
/// <c>UserManager&lt;AppUser&gt;</c>. Los casos de uso nunca ven la entidad de Identity.
/// </summary>
public interface IUserCredentialsService
{
    /// <summary>Valida email + password. <c>null</c> si no existe, la clave no coincide o la cuenta está inactiva.</summary>
    Task<AccountIdentity?> ValidateCredentialsAsync(
        string email, string password, CancellationToken cancellationToken = default);

    /// <summary>Cuenta activa por id. <c>null</c> si no existe o está inactiva.</summary>
    Task<AccountIdentity?> FindActiveByIdAsync(
        Guid userId, CancellationToken cancellationToken = default);
}
