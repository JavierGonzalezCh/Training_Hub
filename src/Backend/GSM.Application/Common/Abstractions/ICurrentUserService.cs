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

    /// <summary>
    /// <c>true</c> si la petición viaja con un token de impersonación: la identidad real es de un
    /// superadmin (<see cref="IsSuperAdmin"/> sigue siendo <c>true</c>) pero opera dentro de
    /// <see cref="GymId"/> y en modo solo lectura.
    /// </summary>
    bool IsImpersonating { get; }

    /// <summary>Id de la sesión de auditoría de impersonación, cuando <see cref="IsImpersonating"/>.</summary>
    Guid? ImpersonationSessionId { get; }

    /// <summary>
    /// <c>true</c> si el usuario tiene el permiso especial de staff. El Owner los tiene todos siempre y
    /// un SuperAdmin impersonando también (<c>GSM-modulo-superadmin.md</c> §V6); un staff, los de su
    /// perfil más sus ajustes (claim <c>perm</c>). Miembros y contextos sin petición, ninguno.
    /// </summary>
    bool HasPermission(StaffPermission permission);
}
