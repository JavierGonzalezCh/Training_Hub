using GSM.Domain.Identity;

namespace GSM.Application.Common.Abstractions;

/// <summary>
/// Identidad del usuario que origina la petición actual, resuelta desde los claims del JWT.
/// La interfaz vive en Application (la consumen los casos de uso); la implementación vive en
/// la API y lee <c>HttpContext</c>. Fuera de una petición HTTP (seeds, jobs, migraciones)
/// se usa una implementación nula donde <see cref="IsAuthenticated"/> es <c>false</c>.
/// </summary>
public interface ICurrentUserService
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }

    /// <summary>Gym del usuario. <c>null</c> únicamente para SuperAdmin o contexto sin petición.</summary>
    Guid? GymId { get; }

    UserRole? Role { get; }

    bool IsSuperAdmin { get; }
}
