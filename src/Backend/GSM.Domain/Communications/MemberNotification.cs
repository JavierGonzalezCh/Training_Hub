using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Communications;

/// <summary>
/// Aviso personal a un miembro, generado por el sistema (clase cancelada, membresía por vencer,
/// logro). Índice único <c>(member_id, type, reference_id)</c>: un handler o la tarea periódica
/// que se repite no duplica el aviso.
/// </summary>
public sealed class MemberNotification : BaseEntity, ITenantEntity
{
    public const int TitleMaxLength = 160;
    public const int BodyMaxLength = 1000;
    public const int LinkMaxLength = 200;

    /// <summary>Días que se conservan antes de purgarlas (tarea periódica, 13.4).</summary>
    public const int RetentionDays = 90;

    private MemberNotification()
    {
    }

    public Guid GymId { get; private set; }

    public Guid MemberId { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>La sesión de clase, la membresía o el logro de miembro que la originó.</summary>
    public Guid ReferenceId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;

    /// <summary>Ruta interna de la app del miembro, p. ej. <c>/app/clases</c>.</summary>
    public string? Link { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public static MemberNotification Create(
        Guid gymId, Guid memberId, NotificationType type, Guid referenceId, string title, string body, string? link)
    {
        if (gymId == Guid.Empty || memberId == Guid.Empty || referenceId == Guid.Empty)
        {
            throw new ValidationDomainException("La notificación necesita gym, miembro y referencia.");
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
            throw new ValidationDomainException("El enlace debe ser una ruta interna de la app.");
        }

        return new MemberNotification
        {
            GymId = gymId,
            MemberId = memberId,
            Type = type,
            ReferenceId = referenceId,
            Title = title.Trim(),
            Body = body.Trim(),
            Link = link,
        };
    }

    public void MarkRead(DateTimeOffset now) => ReadAt ??= now;
}
