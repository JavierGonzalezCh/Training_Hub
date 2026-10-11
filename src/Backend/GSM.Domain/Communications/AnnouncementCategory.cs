namespace GSM.Domain.Communications;

/// <summary>
/// Categoría de un anuncio (CO1). Define qué preferencia del miembro se respeta: Novedades del gym o
/// Promociones. Label en Postgres: <c>communications.announcement_category</c> (snake_case).
/// </summary>
public enum AnnouncementCategory
{
    GymNews,
    Promotions,
}
