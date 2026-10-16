namespace GSM.Domain.Communications;

/// <summary>Ciclo de un anuncio. Label en Postgres: <c>communications.announcement_status</c>.</summary>
public enum AnnouncementStatus
{
    Draft,
    Published,
    Archived,

    /// <summary>CO4: programado; lo publica la tarea periódica a partir de <c>ScheduledFor</c>. Va al final: el orden del enum no se reordena.</summary>
    Scheduled,
}
