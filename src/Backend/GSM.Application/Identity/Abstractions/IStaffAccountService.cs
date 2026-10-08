namespace GSM.Application.Identity.Abstractions;

/// <summary>
/// Cuentas de staff de un gym sobre ASP.NET Identity. Implementado en Infrastructure. Opera sobre el
/// <c>GymAccess</c> de rol staff en ese gym, no sobre la cuenta: desactivar a alguien aquí no le
/// quita sus otros gyms. Los accesos no están bajo el Global Query Filter, así que toda operación
/// recibe el gym explícito; el owner y los miembros quedan fuera de alcance.
/// </summary>
public interface IStaffAccountService
{
    Task<IReadOnlyList<StaffAccount>> ListAsync(Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Da acceso de staff en el gym. Si el email no tiene cuenta, la crea sin contraseña y devuelve un
    /// código de restablecimiento para que la defina. Si ya tiene cuenta, solo agrega el acceso
    /// (<c>Token = null</c>). <c>ConflictException</c> si ya tiene un acceso de equipo en ese gym.
    /// </summary>
    Task<StaffInvitationTicket> CreateAsync(
        Guid gymId, string email, string fullName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Activa o desactiva el acceso. Al desactivar revoca los refresh tokens de ese acceso (los de
    /// otros gyms quedan). <c>NotFoundException</c> si no es staff del gym.
    /// </summary>
    Task<StaffAccount> SetActiveAsync(
        Guid gymId, Guid userId, bool active, CancellationToken cancellationToken = default);
}

public sealed record StaffAccount(Guid Id, string Email, string FullName, bool IsActive);

/// <param name="Token">Código para definir la contraseña; <c>null</c> si la persona ya tenía cuenta.</param>
public sealed record StaffInvitationTicket(StaffAccount Account, string? Token);
