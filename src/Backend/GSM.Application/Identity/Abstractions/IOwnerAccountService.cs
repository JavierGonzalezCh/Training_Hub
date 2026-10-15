using GSM.Domain.Identity;

namespace GSM.Application.Identity.Abstractions;

/// <summary>
/// Owners de un gym (Identity I4, Gyms §6): agregar, ascender un staff, quitar. Implementado en
/// Infrastructure. Opera sobre el <c>GymAccess</c> de rol owner/staff, y recibe el gym explícito (los
/// accesos no están bajo el Global Query Filter). <b>Un gym nunca se queda sin Owner</b>: quitar al último es
/// <c>ConflictException</c>. Todo cambio deja su registro de auditoría (quién, a quién, cuándo).
/// </summary>
public interface IOwnerAccountService
{
    /// <summary>
    /// Da acceso de Owner a un email. Cuenta nueva: nace sin contraseña y el enlace de activación (72 h) abre el
    /// formulario. Con cuenta previa solo se agrega el acceso (el enlace la lleva a entrar). Un ex-Owner con el
    /// acceso desactivado se reactiva. <c>ConflictException</c> si ya es Owner o ya es staff del gym (para eso
    /// está "ascender"), o si la cuenta no puede recibir accesos.
    /// </summary>
    Task<OwnerInvitationResult> InviteAsync(
        Guid gymId, string email, string fullName, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asciende a un staff activo del gym a Owner: pierde su perfil y sus ajustes (el Owner tiene todos los
    /// permisos). <c>NotFoundException</c> si no es staff del gym; <c>ConflictException</c> si está desactivado.
    /// </summary>
    Task<OwnerAccount> PromoteStaffAsync(
        Guid gymId, Guid userId, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Quita a un Owner (a otro o a sí mismo). Sin <paramref name="keepAsStaff"/> se desactiva su acceso; con él
    /// queda como staff con <paramref name="staffProfile"/>. En ambos casos sus sesiones en este gym se cierran.
    /// <c>ConflictException</c> si es el último Owner activo; <c>NotFoundException</c> si no es Owner del gym.
    /// Idempotente para un Owner ya desactivado.
    /// </summary>
    Task<OwnerAccount> RemoveAsync(
        Guid gymId,
        Guid userId,
        bool keepAsStaff,
        StaffProfile staffProfile,
        Guid actorUserId,
        CancellationToken cancellationToken = default);
}

/// <param name="Owner">El Owner dado de alta.</param>
/// <param name="ActivationUrl">Enlace listo para mandar.</param>
/// <param name="HadAccount"><c>true</c> si la persona ya tenía cuenta con contraseña: el enlace solo la lleva a entrar.</param>
public sealed record OwnerInvitationResult(OwnerAccount Owner, string ActivationUrl, bool HadAccount);
