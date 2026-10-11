using GSM.Domain.Common;
using GSM.Domain.Identity;

namespace GSM.Infrastructure.Identity;

/// <summary>
/// Ajuste de un permiso sobre el perfil de un acceso de staff (§5.2): <c>Granted = true</c> agrega un
/// permiso que el perfil no trae, <c>false</c> quita uno que sí trae. Solo se guardan los ajustes; los
/// permisos efectivos = los del perfil + agregados − quitados.
/// </summary>
public sealed class GymAccessPermission : BaseEntity
{
    private GymAccessPermission()
    {
    }

    public Guid GymAccessId { get; private set; }

    public StaffPermission Permission { get; private set; }

    public bool Granted { get; private set; }

    public static GymAccessPermission Create(Guid gymAccessId, StaffPermission permission, bool granted)
        => new() { GymAccessId = gymAccessId, Permission = permission, Granted = granted };

    public void SetGranted(bool granted) => Granted = granted;
}

/// <summary>Qué se hizo sobre el perfil o los permisos de un acceso de staff.</summary>
public enum StaffAccessAuditAction
{
    /// <summary>Se dio acceso de staff con ese perfil.</summary>
    Invited,

    /// <summary>Cambió el perfil (<c>OldValue</c> a <c>NewValue</c>).</summary>
    ProfileChanged,

    /// <summary>La persona ganó un permiso efectivo.</summary>
    PermissionGranted,

    /// <summary>La persona perdió un permiso efectivo: sus sesiones en ese gym se cerraron.</summary>
    PermissionRevoked,

    /// <summary>
    /// Alguien copió el enlace de activación de la persona para mandárselo (<c>NewValue</c>: <c>activation</c>
    /// o <c>login</c> si ya tenía contraseña). El actor es quien lo copió (Owner o SuperAdmin).
    /// </summary>
    LinkCopied,
}

/// <summary>
/// Registro de auditoría de perfiles y permisos de staff (§5.3): quién (<see cref="ActorUserId"/>), a
/// quién (<see cref="TargetUserId"/>), qué y cuándo (<c>CreatedAt</c>). Un cambio puede dejar varias filas
/// (la del perfil y una por cada permiso efectivo que cambió).
/// </summary>
public sealed class StaffAccessAudit : BaseEntity
{
    private StaffAccessAudit()
    {
    }

    public Guid GymId { get; private set; }

    public Guid GymAccessId { get; private set; }

    public Guid TargetUserId { get; private set; }

    public Guid ActorUserId { get; private set; }

    public StaffAccessAuditAction Action { get; private set; }

    /// <summary>Solo en <c>PermissionGranted</c> / <c>PermissionRevoked</c>.</summary>
    public StaffPermission? Permission { get; private set; }

    /// <summary>Perfil anterior, en <c>ProfileChanged</c>.</summary>
    public string? OldValue { get; private set; }

    /// <summary>Perfil nuevo, en <c>Invited</c> y <c>ProfileChanged</c>.</summary>
    public string? NewValue { get; private set; }

    public static StaffAccessAudit Create(
        Guid gymId,
        Guid gymAccessId,
        Guid targetUserId,
        Guid actorUserId,
        StaffAccessAuditAction action,
        StaffPermission? permission = null,
        string? oldValue = null,
        string? newValue = null)
        => new()
        {
            GymId = gymId,
            GymAccessId = gymAccessId,
            TargetUserId = targetUserId,
            ActorUserId = actorUserId,
            Action = action,
            Permission = permission,
            OldValue = oldValue,
            NewValue = newValue,
        };
}
