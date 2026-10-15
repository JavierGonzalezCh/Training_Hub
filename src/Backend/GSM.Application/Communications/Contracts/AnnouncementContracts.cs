using GSM.Domain.Communications;

namespace GSM.Application.Communications.Contracts;

public sealed record CreateAnnouncementRequest(
    string Title,
    string Body,
    bool Pinned,
    DateOnly? ExpiresOn,
    bool SendEmail,
    AnnouncementCategory Category = AnnouncementCategory.GymNews,
    bool Important = false,
    AnnouncementAudienceDto? Audience = null);

/// <summary>Fijar y desfijar tienen sus propios endpoints; aquí solo el contenido.</summary>
public sealed record UpdateAnnouncementRequest(
    string Title,
    string Body,
    DateOnly? ExpiresOn,
    bool SendEmail,
    AnnouncementCategory? Category = null,
    bool? Important = null,
    AnnouncementAudienceDto? Audience = null);

/// <summary>
/// Filtros de la audiencia (CO2). Todo vacío = todos los miembros. Los valores de un filtro suman
/// ("vencidos o en gracia") y los filtros se combinan con "y". <c>Statuses</c>: Current, Expiring, Grace,
/// Expired, Frozen, NoPlan. <c>Tags</c> sin distinguir mayúsculas. Clase: reserva o asistencia en los
/// últimos 30 días. Plan: membresía vigente hoy en ese plan.
/// </summary>
public sealed record AnnouncementAudienceDto(
    IReadOnlyList<string> Statuses,
    IReadOnlyList<Guid> PlanIds,
    IReadOnlyList<Guid> ClassIds,
    IReadOnlyList<string> Tags);

/// <summary>"Llegará a N miembros", con la lista (hasta 200) para revisarla.</summary>
public sealed record AudiencePreviewResponse(int Count, bool Truncated, IReadOnlyList<AudienceMemberItem> Members);

public sealed record AudienceMemberItem(Guid Id, string Name);

/// <summary>Etiquetas en uso entre los miembros activos (para elegir el filtro).</summary>
public sealed record AudienceTagsResponse(IReadOnlyList<string> Tags);

public sealed record ListAnnouncementsQuery
{
    public string? Status { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

/// <summary>Vista del panel (staff).</summary>
public sealed record AnnouncementResponse(
    Guid Id,
    string Title,
    string Body,
    string Status,
    DateTimeOffset? PublishedAt,
    bool Pinned,
    DateOnly? ExpiresOn,
    bool SendEmail,
    DateTimeOffset CreatedAt,
    ReactionCounts Reactions,
    EmailDelivery? Delivery,
    string Category,
    bool Important,
    Guid? ImportantMarkedBy,
    AnnouncementAudienceDto Audience,
    int? RecipientCount);

/// <summary>Vista del feed del miembro: solo lo publicado y vigente.</summary>
public sealed record FeedItem(
    Guid Id,
    string Title,
    string Body,
    DateTimeOffset PublishedAt,
    bool Pinned,
    ReactionCounts Reactions,
    string? MyReaction,
    bool Important);

/// <summary>
/// Totales por tipo, como campos y no como diccionario de enum: NSwag genera mal las colecciones
/// de enum en las respuestas (ver CLAUDE.md, segundo defecto de NSwag).
/// </summary>
public sealed record ReactionCounts(int Like, int Celebrate, int Strong)
{
    public static ReactionCounts None { get; } = new(0, 0, 0);
}

/// <summary>Reacción del miembro: Like, Celebrate o Strong.</summary>
public sealed record ReactRequest(string Kind);

/// <summary>Estado tras reaccionar: totales actualizados y la reacción propia (nula si se quitó).</summary>
public sealed record ReactionResponse(Guid AnnouncementId, ReactionCounts Reactions, string? MyReaction);

/// <summary>No leídos de la app: anuncios nuevos desde la última visita y avisos personales sin leer.</summary>
public sealed record UnreadCountResponse(int Announcements, int Notifications);
