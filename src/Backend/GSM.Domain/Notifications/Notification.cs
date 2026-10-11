using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Notifications;

/// <summary>
/// Un aviso. Dos formas: <b>personal</b> (una fila por persona: <c>RecipientUserId</c> o
/// <c>RecipientMemberId</c>) y <b>de acción del gym</b> (<c>RequiresAction</c> y sin destinatario: una
/// sola fila por gym, que ve todo el staff y se "atiende" para todos). Índice único
/// <c>(type, destinatario, reference_id)</c>: el mismo aviso no sale dos veces aunque el evento se repita.
/// </summary>
public sealed class Notification : BaseEntity, ITenantEntity
{
    public const int TitleMaxLength = 160;
    public const int BodyMaxLength = 1000;
    public const int LinkMaxLength = 200;
    public const int ImageUrlMaxLength = 500;

    /// <summary>Días que se conservan los avisos leídos o atendidos antes de purgarlos.</summary>
    public const int RetentionDays = 90;

    private Notification()
    {
    }

    public Guid GymId { get; private set; }

    public Guid? RecipientUserId { get; private set; }

    public Guid? RecipientMemberId { get; private set; }

    public NotificationType Type { get; private set; }

    public NotificationCategory? Category { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;

    public string? ImageUrl { get; private set; }

    /// <summary>Ruta interna de la app o del panel, p. ej. <c>/app/clases</c> o <c>/members/{id}</c>.</summary>
    public string? Link { get; private set; }

    public NotificationPriority Priority { get; private set; }

    public DateTimeOffset ScheduledFor { get; private set; }

    public NotificationStatus Status { get; private set; } = NotificationStatus.Pending;

    /// <summary>Lo que originó el aviso (la sesión, la membresía, el intento…).</summary>
    public Guid ReferenceId { get; private set; }

    /// <summary>Aviso de acción: queda atendido para todos cuando alguien lo atiende.</summary>
    public bool RequiresAction { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public Guid? ResolvedBy { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>Si está <see cref="NotificationStatus.Held"/>: cuándo termina el horario sin avisos y puede salir.</summary>
    public DateTimeOffset? ReleaseAt { get; private set; }

    public static Notification CreateForMember(
        Guid gymId, Guid memberId, NotificationType type, NotificationCategory? category, Guid referenceId,
        string title, string body, string? link, DateTimeOffset scheduledFor,
        NotificationPriority priority = NotificationPriority.Normal)
    {
        if (memberId == Guid.Empty)
        {
            throw new ValidationDomainException("El aviso necesita un miembro destinatario.");
        }

        return Build(gymId, null, memberId, type, category, referenceId, title, body, link, scheduledFor, priority, false);
    }

    public static Notification CreateForUser(
        Guid gymId, Guid userId, NotificationType type, Guid referenceId,
        string title, string body, string? link, DateTimeOffset scheduledFor,
        NotificationPriority priority = NotificationPriority.Normal)
    {
        if (userId == Guid.Empty)
        {
            throw new ValidationDomainException("El aviso necesita un usuario destinatario.");
        }

        return Build(gymId, userId, null, type, null, referenceId, title, body, link, scheduledFor, priority, false);
    }

    /// <summary>Aviso de acción para todo el staff del gym: una sola fila, sin destinatario.</summary>
    public static Notification CreateGymAction(
        Guid gymId, NotificationType type, Guid referenceId,
        string title, string body, string? link, DateTimeOffset scheduledFor,
        NotificationPriority priority = NotificationPriority.Normal)
        => Build(gymId, null, null, type, null, referenceId, title, body, link, scheduledFor, priority, true);

    private static Notification Build(
        Guid gymId, Guid? userId, Guid? memberId, NotificationType type, NotificationCategory? category,
        Guid referenceId, string title, string body, string? link, DateTimeOffset scheduledFor,
        NotificationPriority priority, bool requiresAction)
    {
        if (gymId == Guid.Empty || referenceId == Guid.Empty)
        {
            throw new ValidationDomainException("El aviso necesita gym y referencia.");
        }

        if (string.IsNullOrWhiteSpace(title) || title.Length > TitleMaxLength)
        {
            throw new ValidationDomainException($"El título debe tener entre 1 y {TitleMaxLength} caracteres.");
        }

        if (body.Length > BodyMaxLength)
        {
            throw new ValidationDomainException($"El texto no puede superar {BodyMaxLength} caracteres.");
        }

        if (link is not null && (!link.StartsWith('/') || link.Length > LinkMaxLength))
        {
            throw new ValidationDomainException("El enlace debe ser una ruta interna.");
        }

        return new Notification
        {
            GymId = gymId,
            RecipientUserId = userId,
            RecipientMemberId = memberId,
            Type = type,
            Category = category,
            ReferenceId = referenceId,
            Title = title.Trim(),
            Body = body.Trim(),
            Link = link,
            Priority = priority,
            ScheduledFor = scheduledFor,
            RequiresAction = requiresAction,
        };
    }

    public void MarkRead(DateTimeOffset now) => ReadAt ??= now;

    /// <summary>Atiende un aviso de acción. <c>false</c> si ya estaba atendido (el primero gana).</summary>
    public bool Resolve(Guid userId, DateTimeOffset now)
    {
        if (!RequiresAction)
        {
            throw new ConflictException("Este aviso no es de acción.");
        }

        if (ResolvedAt is not null)
        {
            return false;
        }

        ResolvedAt = now;
        ResolvedBy = userId;
        return true;
    }

    /// <summary>Actualiza el texto de un aviso aún sin atender (p. ej. el contador de intentos).</summary>
    public void UpdateText(string title, string body)
    {
        if (ResolvedAt is not null)
        {
            return;
        }

        Title = title.Trim();
        Body = body.Trim();
    }

    public void MarkSent(DateTimeOffset now)
    {
        Status = NotificationStatus.Sent;
        SentAt = now;
    }

    /// <summary>
    /// Retiene el envío (push y correo) hasta que termine el horario sin avisos. La bandeja no se ve
    /// afectada: el aviso ya aparece en ella.
    /// </summary>
    public void Hold(DateTimeOffset releaseAt)
    {
        if (Status is NotificationStatus.Pending or NotificationStatus.Held)
        {
            Status = NotificationStatus.Held;
            ReleaseAt = releaseAt;
        }
    }

    public void MarkFailed() => Status = NotificationStatus.Failed;

    public void Cancel()
    {
        if (Status is NotificationStatus.Pending or NotificationStatus.Held)
        {
            Status = NotificationStatus.Cancelled;
        }
    }
}
