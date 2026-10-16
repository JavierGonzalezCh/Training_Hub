namespace GSM.Application.Communications.Public;

/// <summary>Lectura de las novedades del gym que Comunicaciones expone a los compositores (D2).</summary>
public interface IMemberNewsReader
{
    /// <summary>
    /// El anuncio que se le destaca al miembro (el fijado más reciente; si no hay fijados, el último publicado) y
    /// cuántos anuncios vigentes no ha visto. Solo anuncios: los avisos personales son de la bandeja.
    /// </summary>
    Task<MemberNewsView> GetAsync(Guid memberId, CancellationToken cancellationToken = default);
}

/// <param name="Featured">Nulo si el gym no tiene anuncios vigentes para el miembro.</param>
/// <param name="UnreadAnnouncements">Anuncios vigentes publicados después de la última vez que el miembro los vio.</param>
public sealed record MemberNewsView(FeaturedAnnouncementView? Featured, int UnreadAnnouncements);

public sealed record FeaturedAnnouncementView(
    Guid Id,
    string Title,
    bool Pinned,
    bool Important,
    DateTimeOffset PublishedAt);
