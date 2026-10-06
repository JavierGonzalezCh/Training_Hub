namespace GSM.Application.Communications.Contracts;

public sealed record CreateAnnouncementRequest(
    string Title,
    string Body,
    bool Pinned,
    DateOnly? ExpiresOn,
    bool SendEmail);

/// <summary>Fijar y desfijar tienen sus propios endpoints; aquí solo el contenido.</summary>
public sealed record UpdateAnnouncementRequest(
    string Title,
    string Body,
    DateOnly? ExpiresOn,
    bool SendEmail);

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
    EmailDelivery? Delivery);

/// <summary>Vista del feed del miembro: solo lo publicado y vigente.</summary>
public sealed record FeedItem(
    Guid Id,
    string Title,
    string Body,
    DateTimeOffset PublishedAt,
    bool Pinned,
    ReactionCounts Reactions,
    string? MyReaction);

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
