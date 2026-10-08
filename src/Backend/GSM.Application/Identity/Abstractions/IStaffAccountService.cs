using GSM.Domain.Identity;

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
    /// Da acceso de staff en el gym con ese perfil. Si el email no tiene cuenta, la crea sin contraseña y
    /// devuelve un código de activación (72 h, para el enlace <c>/activar</c>) para que la defina. Si ya tiene cuenta, solo agrega el
    /// acceso (<c>Token = null</c>). <c>ConflictException</c> si ya tiene un acceso de equipo en ese gym.
    /// Deja el registro de auditoría (quién invitó, con qué perfil).
    /// </summary>
    Task<StaffInvitationTicket> CreateAsync(
        Guid gymId,
        string email,
        string fullName,
        StaffProfile profile,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Activa o desactiva el acceso. Al desactivar revoca los refresh tokens de ese acceso (los de
    /// otros gyms quedan). <c>NotFoundException</c> si no es staff del gym.
    /// </summary>
    Task<StaffAccount> SetActiveAsync(
        Guid gymId, Guid userId, bool active, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fija el perfil y los permisos efectivos deseados del acceso: los ajustes guardados son la
    /// diferencia entre <paramref name="permissions"/> y lo que trae el perfil. Si se quita algún
    /// permiso efectivo (o el perfil nuevo trae menos), revoca los refresh tokens de ese acceso: la
    /// persona vuelve a entrar y su token nuevo ya no lo trae. Agregar no revoca. Todo cambio deja su
    /// registro de auditoría. <c>NotFoundException</c> si no es staff del gym.
    /// </summary>
    Task<StaffAccount> UpdateAccessAsync(
        Guid gymId,
        Guid userId,
        StaffProfile profile,
        IReadOnlyCollection<StaffPermission> permissions,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Auditoría de perfiles y permisos del gym, la más reciente primero; de una persona si se indica
    /// <paramref name="userId"/>.
    /// </summary>
    Task<IReadOnlyList<StaffAuditEntry>> ListAuditAsync(
        Guid gymId, Guid? userId, int take, CancellationToken cancellationToken = default);
}

/// <param name="Profile">Perfil del acceso.</param>
/// <param name="IsAdjusted"><c>true</c> si tiene permisos agregados o quitados sobre su perfil.</param>
/// <param name="Permissions">Permisos efectivos (perfil + agregados − quitados).</param>
public sealed record StaffAccount(
    Guid Id,
    string Email,
    string FullName,
    bool IsActive,
    StaffProfile Profile,
    bool IsAdjusted,
    IReadOnlyList<StaffPermission> Permissions);

/// <param name="Token">Código de activación para definir la contraseña; <c>null</c> si la persona ya tenía cuenta.</param>
public sealed record StaffInvitationTicket(StaffAccount Account, string? Token);

public sealed record StaffAuditEntry(
    Guid Id,
    Guid TargetUserId,
    string TargetName,
    Guid ActorUserId,
    string ActorName,
    string Action,
    StaffPermission? Permission,
    string? OldValue,
    string? NewValue,
    DateTimeOffset At);
