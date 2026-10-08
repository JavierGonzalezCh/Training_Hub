using GSM.Application.Common.Abstractions;
using GSM.Domain.Identity;

namespace GSM.IntegrationTests;

/// <summary>Doble de <see cref="ICurrentUserService"/> para los tests: identidad fijada a mano.</summary>
public sealed class MutableCurrentUser : ICurrentUserService
{
    public bool IsAuthenticated { get; private set; }

    public Guid? UserId { get; private set; }

    public Guid? GymId { get; private set; }

    public UserRole? Role { get; private set; }

    public bool IsSuperAdmin => Role == UserRole.SuperAdmin;

    public bool IsImpersonating { get; private set; }

    public Guid? ImpersonationSessionId { get; private set; }

    /// <summary>Permisos de un staff fijado a mano (el owner y el superadmin impersonando los tienen todos).</summary>
    public HashSet<StaffPermission> Permissions { get; } = [];

    public bool HasPermission(StaffPermission permission)
        => IsAuthenticated
           && (Role == UserRole.Owner || IsImpersonating || Permissions.Contains(permission));

    public void ActAsSystem()
    {
        Permissions.Clear();
        IsAuthenticated = false;
        UserId = null;
        GymId = null;
        Role = null;
        IsImpersonating = false;
        ImpersonationSessionId = null;
    }

    public void ActAs(Guid userId, Guid? gymId, UserRole role)
    {
        Permissions.Clear();
        IsAuthenticated = true;
        UserId = userId;
        GymId = gymId;
        Role = role;
        IsImpersonating = false;
        ImpersonationSessionId = null;
    }

    /// <summary>Simula un token de impersonación: superadmin real, acotado a <paramref name="gymId"/>.</summary>
    public void ActAsImpersonator(Guid superAdminUserId, Guid gymId, Guid sessionId)
    {
        IsAuthenticated = true;
        UserId = superAdminUserId;
        GymId = gymId;
        Role = UserRole.SuperAdmin;
        IsImpersonating = true;
        ImpersonationSessionId = sessionId;
    }
}
