using GSM.Domain.Members;

namespace GSM.Application.Members.Public;

/// <summary>Lectura que el módulo Members expone a los demás módulos.</summary>
public interface IMemberReader
{
    Task<MemberInfo?> GetAsync(Guid memberId, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>Miembro vinculado a esa cuenta de Identity. <c>null</c> si el usuario no es un miembro.</summary>
    Task<MemberInfo?> ResolveByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<int> GetActiveCountAsync(Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>Datos de contacto de un miembro (Comunicaciones). Nulo si no existe.</summary>
    Task<MemberContactView?> GetContactAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>Miembros activos del gym con email (destinatarios de un anuncio por correo).</summary>
    Task<IReadOnlyList<MemberContactView>> GetEmailContactsAsync(Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>Ids de los miembros activos del gym (audiencia de un anuncio).</summary>
    Task<IReadOnlyList<Guid>> GetActiveMemberIdsAsync(Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Celular de cada miembro en E.164 (<c>+573001234567</c>), listo para <c>wa.me</c>. Quien no tiene
    /// celular o no es interpretable no aparece. Para el botón de WhatsApp (Comunicaciones).
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> GetWhatsAppPhonesAsync(
        IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken = default);

    /// <summary>La invitación más reciente sin canjear del miembro (puede estar vencida: lo decide quien llama). <c>null</c> si no hay.</summary>
    Task<MemberInvitationView?> GetPendingInvitationAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Miembros activos por gym, en una sola consulta (panel de SuperAdmin). Un gym sin miembros
    /// activos no aparece.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> GetActiveCountsByGymAsync(CancellationToken cancellationToken = default);
}

public sealed record MemberInvitationView(string Code, DateTimeOffset ExpiresAt);

public sealed record MemberContactView(
    Guid Id,
    Guid GymId,
    string FirstName,
    string? Email,
    MemberStatus Status);

public sealed record MemberInfo(
    Guid Id,
    Guid GymId,
    Guid? UserId,
    string FirstName,
    string LastName,
    MemberStatus Status);
