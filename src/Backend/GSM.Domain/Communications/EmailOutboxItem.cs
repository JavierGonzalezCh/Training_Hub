using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Communications;

/// <summary>Qué originó un correo de comunicaciones. Label: <c>communications.email_kind</c> (snake_case).</summary>
public enum EmailKind
{
    Announcement,
    ClassCancelled,
    MembershipExpiring,
}

/// <summary>Estado de un correo en la cola. Label: <c>communications.email_status</c>.</summary>
public enum EmailStatus
{
    Pending,
    Sent,
    Failed,
}

/// <summary>
/// Correo encolado. Los casos de uso y los handlers encolan; nunca envían dentro de la petición.
/// La tarea periódica vacía la cola en lotes. Índice único <c>(member_id, kind, reference_id)</c>:
/// el mismo aviso no se envía dos veces aunque se encole dos veces.
/// </summary>
public sealed class EmailOutboxItem : BaseEntity, ITenantEntity
{
    public const int MaxAttempts = 3;
    public const int SubjectMaxLength = 200;

    private EmailOutboxItem()
    {
    }

    public Guid GymId { get; private set; }

    public Guid MemberId { get; private set; }

    public string ToAddress { get; private set; } = string.Empty;

    public string Subject { get; private set; } = string.Empty;

    public string HtmlBody { get; private set; } = string.Empty;

    public string TextBody { get; private set; } = string.Empty;

    public EmailKind Kind { get; private set; }

    /// <summary>El anuncio, la sesión de clase o la membresía que lo originó.</summary>
    public Guid ReferenceId { get; private set; }

    public EmailStatus Status { get; private set; } = EmailStatus.Pending;

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public static EmailOutboxItem Create(
        Guid gymId, Guid memberId, string toAddress, string subject, string htmlBody, string textBody,
        EmailKind kind, Guid referenceId)
    {
        if (gymId == Guid.Empty || memberId == Guid.Empty || referenceId == Guid.Empty)
        {
            throw new ValidationDomainException("El correo necesita gym, miembro y referencia.");
        }

        if (string.IsNullOrWhiteSpace(toAddress))
        {
            throw new ValidationDomainException("El correo necesita un destinatario.");
        }

        return new EmailOutboxItem
        {
            GymId = gymId,
            MemberId = memberId,
            ToAddress = toAddress.Trim(),
            Subject = subject.Length > SubjectMaxLength ? subject[..SubjectMaxLength] : subject,
            HtmlBody = htmlBody,
            TextBody = textBody,
            Kind = kind,
            ReferenceId = referenceId,
        };
    }

    public void MarkSent(DateTimeOffset now)
    {
        Status = EmailStatus.Sent;
        SentAt = now;
        LastError = null;
        Attempts++;
    }

    /// <summary>Al tercer fallo queda <see cref="EmailStatus.Failed"/> y la tarea deja de reintentarlo.</summary>
    public void MarkAttemptFailed(string error)
    {
        Attempts++;
        LastError = error.Length > 500 ? error[..500] : error;
        if (Attempts >= MaxAttempts)
        {
            Status = EmailStatus.Failed;
        }
    }
}
