using GSM.Domain.Identity;

namespace GSM.Application.Identity.Public;

/// <summary>
/// Única escritura que Identity expone a otros módulos: dar de alta una persona con acceso a un gym
/// (owner en el alta del gym, miembro al canjear su invitación). Se ejecuta dentro de la misma
/// transacción del caso de uso. Si el email ya tiene cuenta, no se crea otra: se agrega el acceso.
/// </summary>
public interface IUserProvisioning
{
    /// <summary>
    /// Alta de owner/staff. Cuenta nueva: se crea con <paramref name="password"/>. Cuenta existente: se
    /// reutiliza (su contraseña no se toca ni se pide: quien da el acceso no la conoce) y solo se agrega
    /// el <c>GymAccess</c>. <c>ConflictException</c> si ya tiene un acceso de equipo en ese gym.
    /// </summary>
    Task<Guid> CreateGymUserAsync(
        string email,
        string fullName,
        string password,
        UserRole role,
        Guid gymId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Alta de un miembro que canjea su invitación. Cuenta nueva: se crea con la contraseña. Cuenta
    /// existente (es miembro de otro gym): <paramref name="password"/> debe ser la suya
    /// (<c>UnauthorizedDomainException</c> si no) y solo se agrega el acceso de miembro.
    /// </summary>
    Task<Guid> CreateMemberUserAsync(
        string email,
        string fullName,
        string password,
        Guid gymId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// FL1: primer ingreso de un miembro con su documento. Crea la cuenta <b>sin contraseña</b> y con
    /// <c>MustChangePassword</c>, con acceso de miembro en cada gym de <paramref name="gymIds"/>.
    /// <c>ConflictException</c> si el email ya tiene cuenta (el primer ingreso es solo para quien no la tiene).
    /// </summary>
    Task<Guid> CreatePendingMemberUserAsync(
        string email, string fullName, IReadOnlyCollection<Guid> gymIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Alta del Owner de un gym nuevo <b>sin contraseña</b> (Gyms §4): la cuenta nace sin ella y el Owner la
    /// define con el enlace de activación (72 h). Si el email ya tiene cuenta, solo se agrega el acceso y el
    /// enlace lo lleva a entrar. <c>ConflictException</c> si la cuenta no puede recibir accesos.
    /// </summary>
    Task<OwnerInvitation> InviteGymOwnerAsync(
        string email, string fullName, Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>El SuperAdmin: cuenta global sin accesos.</summary>
    Task<Guid> CreateSuperAdminAsync(
        string email, string fullName, string password, CancellationToken cancellationToken = default);
}

/// <param name="UserId">La cuenta del Owner.</param>
/// <param name="ActivationUrl">Enlace listo para mandar. Con cuenta nueva abre el formulario de contraseña.</param>
/// <param name="HadAccount"><c>true</c> si la persona ya tenía cuenta: el enlace solo la lleva a entrar.</param>
public sealed record OwnerInvitation(Guid UserId, string ActivationUrl, bool HadAccount);
