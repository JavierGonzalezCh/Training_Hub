namespace GSM.Application.Identity.Abstractions;

/// <summary>
/// Cuentas de staff de un gym sobre ASP.NET Identity. Implementado en Infrastructure. Los usuarios
/// no están bajo el Global Query Filter, así que toda operación recibe el gym explícito y solo
/// ve usuarios con rol Staff de ese gym: el owner y los socios quedan fuera de alcance.
/// </summary>
public interface IStaffAccountService
{
    Task<IReadOnlyList<StaffAccount>> ListAsync(Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Crea la cuenta sin contraseña y devuelve un código de restablecimiento para que el usuario
    /// la defina. Lanza <c>ConflictException</c> si el email ya existe.
    /// </summary>
    Task<StaffInvitationTicket> CreateAsync(
        Guid gymId, string email, string fullName, CancellationToken cancellationToken = default);

    /// <summary>Activa o desactiva la cuenta. Al desactivar revoca sus refresh tokens. <c>NotFoundException</c> si no es staff del gym.</summary>
    Task<StaffAccount> SetActiveAsync(
        Guid gymId, Guid userId, bool active, CancellationToken cancellationToken = default);
}

public sealed record StaffAccount(Guid Id, string Email, string FullName, bool IsActive);

public sealed record StaffInvitationTicket(StaffAccount Account, string Token);
