using GSM.Domain.Identity;

namespace GSM.Application.Common.Abstractions;

/// <summary>
/// Identidad del usuario que origina la petición actual, resuelta desde los claims del JWT.
/// La implementación vive en la API (lee <c>HttpContext</c>). Fuera de una petición HTTP
/// (seeds, jobs) se usa una implementación nula donde <see cref="IsAuthenticated"/> es <c>false</c>.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }

    /// <summary>Gym del usuario. <c>null</c> únicamente para SuperAdmin.</summary>
    Guid? GymId { get; }

    UserRole? Role { get; }

    bool IsSuperAdmin { get; }
}
