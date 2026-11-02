using GSM.Application.Common;
using GSM.Domain.Identity;

namespace GSM.Application.Identity.Abstractions;

/// <summary>
/// Soporte del SuperAdmin sobre las cuentas de un gym: listarlas y fijar una contraseña sin correo (sin Resend
/// no hay restablecimiento por enlace). Implementado en Infrastructure sobre <c>UserManager&lt;AppUser&gt;</c>.
/// </summary>
public interface IAdminAccountService
{
    /// <summary>
    /// Los accesos del gym (owners, staff y miembros), uno por fila; una persona que es staff y miembro sale dos
    /// veces. <paramref name="search"/> filtra por nombre o email.
    /// </summary>
    Task<PagedResult<GymAccount>> ListAsync(
        Guid gymId, string? search, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fija la contraseña de una persona con acceso al gym: aplica la política de contraseñas, levanta el bloqueo
    /// por intentos y cierra todas sus sesiones. La contraseña es de la persona, así que vale en todos sus gyms.
    /// <c>NotFoundException</c> si no tiene acceso al gym; <c>ForbiddenDomainException</c> si es el SuperAdmin;
    /// <c>ValidationDomainException</c> si la contraseña no cumple la política. Deja la auditoría con
    /// <paramref name="actorUserId"/>.
    /// </summary>
    Task SetPasswordAsync(
        Guid gymId, Guid userId, string newPassword, Guid actorUserId, CancellationToken cancellationToken = default);
}

public sealed record GymAccount(
    Guid UserId, string Email, string FullName, UserRole Role, bool IsActive, bool HasPassword);
