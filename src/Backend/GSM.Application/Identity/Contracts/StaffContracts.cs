namespace GSM.Application.Identity.Contracts;

/// <param name="Profile">Perfil (<c>reception</c>, <c>trainer</c>, <c>administrator</c>); sin indicarlo, <c>reception</c>.</param>
public sealed record CreateStaffRequest(string Email, string FullName, string? Profile = null);

/// <param name="Profile">Perfil del acceso: <c>reception</c>, <c>trainer</c> o <c>administrator</c>.</param>
/// <param name="IsAdjusted"><c>true</c> si sus permisos difieren de los del perfil ("Entrenador (ajustado)").</param>
/// <param name="Permissions">Permisos efectivos (<c>view_member_health</c>, <c>trainer</c>, <c>manage_attendance</c>, <c>void_payments</c>, <c>view_finances</c>).</param>
public sealed record StaffResponse(
    Guid Id,
    string Email,
    string FullName,
    bool IsActive,
    string Profile,
    bool IsAdjusted,
    IReadOnlyList<string> Permissions);

/// <summary>
/// Fija el perfil y los permisos efectivos deseados. Los permisos que difieren del perfil quedan como
/// ajustes; quitar alguno cierra las sesiones de la persona en este gym.
/// </summary>
public sealed record UpdateStaffPermissionsRequest(string Profile, IReadOnlyList<string> Permissions);

/// <summary>Un perfil predefinido y los permisos que trae, para que la pantalla no los duplique.</summary>
public sealed record StaffProfileInfo(string Profile, IReadOnlyList<string> Permissions);

/// <param name="Action"><c>invited</c>, <c>profile_changed</c>, <c>permission_granted</c> o <c>permission_revoked</c>.</param>
/// <param name="Permission">Solo en <c>permission_granted</c> / <c>permission_revoked</c>.</param>
/// <param name="OldValue">Perfil anterior, en <c>profile_changed</c>.</param>
/// <param name="NewValue">Perfil nuevo, en <c>invited</c> y <c>profile_changed</c>.</param>
public sealed record StaffAuditResponse(
    Guid Id,
    Guid TargetUserId,
    string TargetName,
    Guid ActorUserId,
    string ActorName,
    string Action,
    string? Permission,
    string? OldValue,
    string? NewValue,
    DateTimeOffset At);
