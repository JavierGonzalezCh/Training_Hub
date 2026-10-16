using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Communications;

/// <summary>
/// Anuncio del gym a todos sus miembros (etapa 13). Nace como borrador; publicarlo fija
/// <see cref="PublishedAt"/> y, si <see cref="SendEmail"/>, dispara el envío por email (13.4).
/// Archivarlo lo oculta del feed, pero los correos ya enviados no se recuperan.
/// </summary>
public sealed class Announcement : BaseEntity, ITenantEntity
{
    public const int TitleMinLength = 3;
    public const int TitleMaxLength = 120;
    public const int BodyMaxLength = 4000;

    /// <summary>Más fijados que esto equivale a no fijar nada.</summary>
    public const int MaxPinned = 3;

    private Announcement()
    {
    }

    public Guid GymId { get; private set; }

    public Guid AuthorUserId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    /// <summary>Texto plano con saltos de línea; sin HTML.</summary>
    public string Body { get; private set; } = string.Empty;

    public AnnouncementStatus Status { get; private set; } = AnnouncementStatus.Draft;

    public DateTimeOffset? PublishedAt { get; private set; }

    public bool Pinned { get; private set; }

    /// <summary>Último día de negocio en que se muestra. Nulo = no caduca.</summary>
    public DateOnly? ExpiresOn { get; private set; }

    public bool SendEmail { get; private set; }

    /// <summary>Novedades del gym o Promociones: define qué preferencia del miembro se respeta (CO1).</summary>
    public AnnouncementCategory Category { get; private set; } = AnnouncementCategory.GymNews;

    /// <summary>
    /// Anuncio importante (decidido 2026-10-11): solo lo marca el Owner. Se salta el horario sin avisos y las
    /// preferencias de Novedades/Promociones, y queda fijo arriba en el feed hasta <see cref="ExpiresOn"/>.
    /// </summary>
    public bool IsImportant { get; private set; }

    /// <summary>Quién marcó el anuncio como importante (auditoría). Nulo si no lo es.</summary>
    public Guid? ImportantMarkedBy { get; private set; }

    /// <summary>
    /// CO2: filtros de la audiencia. Vacíos = todos los miembros. Dentro de un filtro, los valores suman
    /// ("vencidos o en gracia"); entre filtros se combinan con "y".
    /// </summary>
    public AnnouncementAudience Audience { get; private set; } = AnnouncementAudience.Everyone;

    /// <summary>
    /// Verdadero si <see cref="Audience"/> tiene algún filtro. Entonces los destinatarios se fijan al publicar
    /// en <c>announcement_recipients</c>; si no, el anuncio lo ve todo el gym (también quien entre después).
    /// </summary>
    public bool IsSegmented { get; private set; }

    /// <summary>A cuántos miembros llegó, fijado al publicar. Solo si <see cref="IsSegmented"/>.</summary>
    public int? RecipientCount { get; private set; }

    /// <summary>CO4: instante (UTC) a partir del cual la tarea periódica lo publica. Solo en estado <c>Scheduled</c>.</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>CO4: URL pública de la imagen (R2). Nula = sin imagen.</summary>
    public string? ImageUrl { get; private set; }

    /// <summary>Todavía no salió: borrador o programado. En este estado se edita audiencia, tipo y correo.</summary>
    public bool IsUnsent => Status is AnnouncementStatus.Draft or AnnouncementStatus.Scheduled;

    public static Announcement CreateDraft(
        Guid gymId, Guid authorUserId, string title, string body, bool pinned, DateOnly? expiresOn, bool sendEmail,
        AnnouncementCategory category = AnnouncementCategory.GymNews, bool important = false,
        AnnouncementAudience? audience = null)
    {
        if (gymId == Guid.Empty)
        {
            throw new ValidationDomainException("El anuncio necesita un gym.");
        }

        var announcement = new Announcement
        {
            GymId = gymId,
            AuthorUserId = authorUserId,
            Pinned = pinned,
            Category = category,
        };
        announcement.Apply(title, body, expiresOn, sendEmail);
        announcement.SetAudience(audience);
        if (important)
        {
            announcement.SetImportant(true, authorUserId);
        }

        return announcement;
    }

    /// <summary>
    /// C8: anuncio automático de "clase nueva". Su id es el que se pasa (derivado de la clase), así que repetir el
    /// evento no publica dos veces: el manejador busca primero por ese id.
    /// </summary>
    public static Announcement CreateForNewClass(
        Guid id, Guid gymId, Guid authorUserId, string title, string body, DateOnly? expiresOn)
    {
        var announcement = CreateDraft(
            gymId, authorUserId, title, body, pinned: false, expiresOn, sendEmail: false, AnnouncementCategory.GymNews);
        announcement.Id = id;
        return announcement;
    }

    /// <summary>
    /// Marca o desmarca el anuncio como importante, solo mientras es borrador: ya publicado, decidió a quién
    /// se le avisó y cómo, y no se cambia. El permiso (solo Owner) lo comprueba el caso de uso.
    /// </summary>
    public void SetImportant(bool important, Guid markedBy)
    {
        if (important == IsImportant)
        {
            return;
        }

        if (!IsUnsent)
        {
            throw new ConflictException("Un anuncio publicado ya no puede cambiar de importante.");
        }

        // Importante se salta las preferencias; una promoción necesita la autorización previa del
        // miembro (Ley 1581), así que nunca puede serlo (decidido 2026-10-12).
        if (important && Category == AnnouncementCategory.Promotions)
        {
            throw new ValidationDomainException("Una promoción no puede marcarse como importante: solo las novedades del gym.");
        }

        IsImportant = important;
        ImportantMarkedBy = important ? markedBy : null;
    }

    /// <summary>
    /// Fija los filtros de la audiencia, solo mientras es borrador: ya publicado, los destinatarios están
    /// fijados y no se cambian. <c>null</c> o vacía = todos.
    /// </summary>
    public void SetAudience(AnnouncementAudience? audience)
    {
        if (!IsUnsent)
        {
            throw new ConflictException("Un anuncio publicado ya no puede cambiar de audiencia.");
        }

        Audience = audience ?? AnnouncementAudience.Everyone;
        IsSegmented = !Audience.IsEveryone;
    }

    /// <summary>Anota a cuántos miembros llegó un anuncio segmentado. Lo llama el caso de uso al publicar.</summary>
    public void SetRecipientCount(int count)
    {
        if (count < 0)
        {
            throw new ValidationDomainException("La cantidad de destinatarios no puede ser negativa.");
        }

        RecipientCount = IsSegmented ? count : null;
    }

    /// <summary>
    /// Cambia el contenido. Sobre un anuncio publicado cambia lo que se ve en la app, pero no
    /// reenvía correos: por eso <paramref name="sendEmail"/> solo cuenta mientras es borrador.
    /// </summary>
    public void Edit(
        string title, string body, DateOnly? expiresOn, bool sendEmail, AnnouncementCategory? category = null)
    {
        EnsureNotArchived("editar");
        if (category is { } newCategory && IsUnsent)
        {
            if (newCategory == AnnouncementCategory.Promotions && IsImportant)
            {
                throw new ValidationDomainException("Una promoción no puede marcarse como importante: solo las novedades del gym.");
            }

            // Publicado, la categoría ya decidió a quién se le avisó: no cambia.
            Category = newCategory;
        }

        Apply(title, body, expiresOn, IsUnsent ? sendEmail : SendEmail);
    }

    public void Publish(DateTimeOffset now, DateOnly today)
    {
        EnsureNotArchived("publicar");
        if (Status == AnnouncementStatus.Published)
        {
            throw new ConflictException("El anuncio ya está publicado.");
        }

        if (ExpiresOn is { } expires && expires < today)
        {
            throw new ValidationDomainException("La fecha de caducidad ya pasó: cámbiala antes de publicar.");
        }

        Status = AnnouncementStatus.Published;
        PublishedAt = now;
        ScheduledFor = null;
    }

    /// <summary>
    /// CO4: programa (o reprograma) la publicación. <paramref name="scheduledDay"/> es el día de negocio de
    /// <paramref name="at"/> en la zona del gym (lo calcula el caso de uso con <c>IClock</c>). Debe ser futuro
    /// y no pasar la caducidad. Publicado o archivado → 409.
    /// </summary>
    public void Schedule(DateTimeOffset at, DateTimeOffset now, DateOnly scheduledDay)
    {
        if (!IsUnsent)
        {
            throw new ConflictException("Solo se puede programar un borrador o reprogramar uno ya programado.");
        }

        if (at <= now)
        {
            throw new ValidationDomainException("La hora de publicación debe ser futura.");
        }

        if (ExpiresOn is { } expires && expires < scheduledDay)
        {
            throw new ValidationDomainException("El anuncio caducaría antes de la hora programada: cambia la fecha «Visible hasta».");
        }

        Status = AnnouncementStatus.Scheduled;
        ScheduledFor = at;
    }

    /// <summary>CO4: cancela la programación y lo devuelve a borrador. Idempotente sobre un borrador.</summary>
    public void Unschedule()
    {
        if (Status == AnnouncementStatus.Draft)
        {
            return;
        }

        if (Status != AnnouncementStatus.Scheduled)
        {
            throw new ConflictException("Solo se puede cancelar la programación de un anuncio programado.");
        }

        Status = AnnouncementStatus.Draft;
        ScheduledFor = null;
    }

    /// <summary>CO4: pone la imagen (ya subida al almacenamiento). Cambia lo que se ve, no reenvía nada.</summary>
    public void SetImage(string url)
    {
        EnsureNotArchived("cambiar la imagen de");
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ValidationDomainException("La URL de la imagen es obligatoria.");
        }

        ImageUrl = url;
    }

    public void RemoveImage()
    {
        EnsureNotArchived("cambiar la imagen de");
        ImageUrl = null;
    }

    public void Archive()
    {
        if (Status == AnnouncementStatus.Archived)
        {
            return;
        }

        Status = AnnouncementStatus.Archived;
        Pinned = false;
        ScheduledFor = null;
    }

    /// <summary>El límite de fijados por gym lo comprueba el caso de uso, que ve a los demás.</summary>
    public void Pin()
    {
        EnsureNotArchived("fijar");
        Pinned = true;
    }

    public void Unpin() => Pinned = false;

    /// <summary>Visible en el feed del miembro ese día de negocio.</summary>
    public bool IsVisibleOn(DateOnly today)
        => Status == AnnouncementStatus.Published && (ExpiresOn is null || ExpiresOn >= today);

    private void Apply(string title, string body, DateOnly? expiresOn, bool sendEmail)
    {
        var normalizedTitle = (title ?? string.Empty).Trim();
        if (normalizedTitle.Length is < TitleMinLength or > TitleMaxLength)
        {
            throw new ValidationDomainException(
                $"El título debe tener entre {TitleMinLength} y {TitleMaxLength} caracteres.");
        }

        var normalizedBody = (body ?? string.Empty).Trim();
        if (normalizedBody.Length is 0 or > BodyMaxLength)
        {
            throw new ValidationDomainException($"El texto debe tener entre 1 y {BodyMaxLength} caracteres.");
        }

        Title = normalizedTitle;
        Body = normalizedBody;
        ExpiresOn = expiresOn;
        SendEmail = sendEmail;
    }

    private void EnsureNotArchived(string action)
    {
        if (Status == AnnouncementStatus.Archived)
        {
            throw new ConflictException($"No se puede {action} un anuncio archivado.");
        }
    }
}
