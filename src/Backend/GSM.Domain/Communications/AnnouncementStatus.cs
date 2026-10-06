namespace GSM.Domain.Communications;

/// <summary>Ciclo de un anuncio. Label en Postgres: <c>communications.announcement_status</c>.</summary>
public enum AnnouncementStatus
{
    Draft,
    Published,
    Archived,
}
