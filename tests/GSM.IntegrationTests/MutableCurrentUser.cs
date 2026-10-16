using GSM.Application.Common.Abstractions;
using GSM.Domain.Identity;

namespace GSM.IntegrationTests;

/// <summary>Doble de <see cref="ICurrentUserService"/> para los tests: identidad fijada a mano.</summary>
public sealed class MutableCurrentUser : ICurrentUserService
{
    // Respeta GymExecutionScope igual que el CurrentUser real: dentro del ámbito es "sistema en ese gym".
    private static bool Scoped => GymExecutionScope.Current is not null;

    private bool _isAuthenticated;
    private Guid? _userId;
    private Guid? _gymId;
    private UserRole? _role;
    private bool _isImpersonating;
    private Guid? _impersonationSessionId;

    public bool IsAuthenticated { get => !Scoped && _isAuthenticated; private set => _isAuthenticated = value; }

    public Guid? UserId { get => Scoped ? null : _userId; private set => _userId = value; }

    public Guid? GymId { get => GymExecutionScope.Current ?? _gymId; private set => _gymId = value; }

    public UserRole? Role { get => Scoped ? null : _role; private set => _role = value; }

    public bool IsSuperAdmin => Role == UserRole.SuperAdmin;

    public bool IsImpersonating { get => !Scoped && _isImpersonating; private set => _isImpersonating = value; }

    public Guid? ImpersonationSessionId { get => Scoped ? null : _impersonationSessionId; private set => _impersonationSessionId = value; }

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
