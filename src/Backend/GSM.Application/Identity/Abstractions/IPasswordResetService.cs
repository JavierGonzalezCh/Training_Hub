namespace GSM.Application.Identity.Abstractions;

/// <summary>Restablecimiento y activación de contraseña sobre ASP.NET Identity. Implementado en Infrastructure.</summary>
public interface IPasswordResetService
{
    /// <summary>Genera un token de restablecimiento (24 h). <c>null</c> si el email no corresponde a una cuenta activa.</summary>
    Task<PasswordResetTicket?> CreateTokenAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Aplica la nueva contraseña, levanta el bloqueo por intentos y cierra todas las sesiones de la
    /// persona. Lanza <c>ValidationDomainException</c> si el token no es válido.
    /// </summary>
    Task ResetAsync(string email, string token, string newPassword, CancellationToken cancellationToken = default);

    /// <summary>
    /// Activa una cuenta invitada (staff, owner) con el código del enlace de activación (72 h): fija la
    /// contraseña y devuelve la cuenta para iniciar su sesión. Lanza <c>ValidationDomainException</c> si el
    /// código no es válido, venció o ya se usó.
    /// </summary>
    Task<AccountIdentity> ActivateAsync(
        string email, string code, string newPassword, CancellationToken cancellationToken = default);
}

public sealed record PasswordResetTicket(string Email, string FullName, string Token);
