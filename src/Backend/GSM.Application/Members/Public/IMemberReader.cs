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
    /// ¿Tiene el miembro una autorización de tratamiento de datos aceptada y no revocada? Es el
    /// candado de las secciones de salud (M3) y medidas (M4): sin ella no se guardan ni se leen.
    /// Basta con haber aceptado cualquier versión; publicar un texto nuevo no la anula (la ficha
    /// solo avisa que falta aceptar la vigente).
    /// </summary>
    Task<bool> HasDataProcessingConsentAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// ¿Tiene la exoneración de responsabilidad aceptada (cualquier versión)? Recepción avisa
    /// "falta la exoneración" cuando es falso. Sin más detalle.
    /// </summary>
    Task<bool> HasLiabilityWaiverAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Miembros activos por gym, en una sola consulta (panel de SuperAdmin). Un gym sin miembros
    /// activos no aparece.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> GetActiveCountsByGymAsync(CancellationToken cancellationToken = default);
}

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
