using GSM.Domain.Identity;

namespace GSM.Application.Identity.Abstractions;

/// <summary>
/// Arma los enlaces públicos de la cuenta (§4) sobre la dirección pública del panel
/// (<c>Identity:PublicWebBaseUrl</c>). Sin esa configuración devuelve rutas relativas.
/// </summary>
public interface IAccountLinkBuilder
{
    /// <summary><c>/restablecer?email=…&amp;code=…</c> (vence a las 24 h).</summary>
    string ResetUrl(string email, string code);

    /// <summary>
    /// <c>/activar?email=…&amp;code=…</c> (vence a las 72 h). Sin código, el enlace es el de una persona que
    /// ya tiene cuenta: la pantalla le pide entrar en vez de crear contraseña.
    /// </summary>
    string ActivationUrl(string email, string? code);
}

/// <summary>
/// "Copiar enlace" de staff y Owners (§4): entrega el enlace para mandarlo por WhatsApp y deja registrado
/// quién lo copió. Implementado en Infrastructure.
/// </summary>
public interface IAccountLinkService
{
    /// <summary>Los accesos de owner del gym, para la ficha del SuperAdmin.</summary>
    Task<IReadOnlyList<OwnerAccount>> ListOwnersAsync(Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enlace de activación nuevo (72 h) para el staff o el owner del gym; si la persona ya tiene
    /// contraseña, el enlace pide entrar (<c>RequiresLogin</c>). <c>NotFoundException</c> si no tiene ese
    /// acceso en el gym; <c>ConflictException</c> si el acceso o la cuenta están desactivados. Deja el
    /// registro de auditoría con <paramref name="actorUserId"/>.
    /// </summary>
    Task<AccountLink> CopyLinkAsync(
        Guid gymId, Guid userId, UserRole role, Guid actorUserId, CancellationToken cancellationToken = default);
}

public sealed record OwnerAccount(Guid UserId, string Email, string FullName, bool IsActive, bool HasPassword);

/// <param name="Url">El enlace listo para copiar.</param>
/// <param name="RequiresLogin"><c>true</c> si la persona ya tiene contraseña: el enlace la lleva a entrar.</param>
/// <param name="ExpiresAt">Cuándo vence; <c>null</c> si no vence (<paramref name="RequiresLogin"/>).</param>
public sealed record AccountLink(string Url, bool RequiresLogin, DateTimeOffset? ExpiresAt);
