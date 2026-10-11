namespace GSM.Application.Identity.Abstractions;

/// <summary>
/// Acceso al store de usuarios de ASP.NET Identity. Implementada en Infrastructure sobre
/// <c>UserManager&lt;AppUser&gt;</c>. Los casos de uso nunca ven la entidad de Identity.
/// </summary>
public interface IUserCredentialsService
{
    /// <summary>
    /// Valida email + password aplicando el bloqueo por intentos (§7): 5 contraseñas incorrectas seguidas
    /// bloquean la cuenta 15 minutos. <c>Account = null</c> si no existe, la clave no coincide, la cuenta está
    /// inactiva o está bloqueada (el llamador no distingue los casos hacia afuera).
    /// </summary>
    Task<CredentialCheck> SignInAsync(string email, string password, CancellationToken cancellationToken = default);

    /// <summary>Atajo de <see cref="SignInAsync"/> que solo devuelve la cuenta.</summary>
    Task<AccountIdentity?> ValidateCredentialsAsync(
        string email, string password, CancellationToken cancellationToken = default);

    /// <summary>Cuenta activa por id. <c>null</c> si no existe o está inactiva.</summary>
    Task<AccountIdentity?> FindActiveByIdAsync(
        Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cambia la contraseña de quien tiene sesión, pidiendo la actual. Una actual incorrecta cuenta como
    /// intento fallido (una sesión robada no puede adivinarla sin límite).
    /// </summary>
    Task<PasswordChangeOutcome> ChangePasswordAsync(
        Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default);
}

/// <param name="Account">La cuenta si las credenciales valen; <c>null</c> si no.</param>
/// <param name="LockedOutNow">
/// <c>true</c> solo en el intento que acaba de bloquear la cuenta: el llamador avisa al dueño por correo.
/// </param>
/// <param name="LockedAccount">Datos de la cuenta bloqueada (para el aviso); solo si <paramref name="LockedOutNow"/>.</param>
public sealed record CredentialCheck(
    AccountIdentity? Account, bool LockedOutNow = false, AccountIdentity? LockedAccount = null);

public sealed record PasswordChangeOutcome(
    bool Succeeded,
    bool WrongCurrentPassword = false,
    bool LockedOutNow = false,
    string? Error = null);
