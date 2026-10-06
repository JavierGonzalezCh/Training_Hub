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

    public static Announcement CreateDraft(
        Guid gymId, Guid authorUserId, string title, string body, bool pinned, DateOnly? expiresOn, bool sendEmail)
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
        };
        announcement.Apply(title, body, expiresOn, sendEmail);
        return announcement;
    }

    /// <summary>
    /// Cambia el contenido. Sobre un anuncio publicado cambia lo que se ve en la app, pero no
    /// reenvía correos: por eso <paramref name="sendEmail"/> solo cuenta mientras es borrador.
    /// </summary>
    public void Edit(string title, string body, DateOnly? expiresOn, bool sendEmail)
    {
        EnsureNotArchived("editar");
        Apply(title, body, expiresOn, Status == AnnouncementStatus.Draft ? sendEmail : SendEmail);
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
    }

    public void Archive()
    {
        if (Status == AnnouncementStatus.Archived)
        {
            return;
        }

        Status = AnnouncementStatus.Archived;
        Pinned = false;
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
