using GSM.Domain.Members;

namespace GSM.Application.Members.Abstractions;

/// <summary>Acceso a los textos de autorización del gym y a las aceptaciones de los miembros. Sin <c>SaveChanges</c>.</summary>
public interface IConsentRepository
{
    /// <summary>La versión más reciente publicada por el gym en contexto para ese tipo. Nulo si no ha publicado ninguna.</summary>
    Task<ConsentDocument?> GetLatestDocumentAsync(ConsentType type, CancellationToken cancellationToken = default);

    void Add(ConsentDocument document);

    /// <summary>Todas las aceptaciones del miembro, de la más reciente a la más antigua.</summary>
    Task<IReadOnlyList<MemberConsent>> GetForMemberAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>La aceptación más reciente del miembro para ese tipo (revocada o no). Nulo si nunca aceptó.</summary>
    Task<MemberConsent?> GetLatestAsync(Guid memberId, ConsentType type, CancellationToken cancellationToken = default);

    void Add(MemberConsent consent);
}
