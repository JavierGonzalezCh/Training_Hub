namespace GSM.Application.Identity.Abstractions;

/// <summary>Restablecimiento de contraseña sobre ASP.NET Identity. Implementado en Infrastructure.</summary>
public interface IPasswordResetService
{
    /// <summary>Genera un token de restablecimiento. <c>null</c> si el email no corresponde a una cuenta activa.</summary>
    Task<PasswordResetTicket?> CreateTokenAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Aplica la nueva contraseña. Lanza <c>ValidationDomainException</c> si el token no es válido.</summary>
    Task ResetAsync(string email, string token, string newPassword, CancellationToken cancellationToken = default);
}

public sealed record PasswordResetTicket(string Email, string FullName, string Token);
