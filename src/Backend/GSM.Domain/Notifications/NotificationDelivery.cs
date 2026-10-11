using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Notifications;

/// <summary>
/// Un envío de un aviso por un canal. En N1 solo correo: lleva el mensaje ya compuesto
/// (destinatario, asunto y cuerpo) para que el dispatcher no dependa de otros módulos al enviar.
/// </summary>
public sealed class NotificationDelivery : BaseEntity, ITenantEntity
{
    public const int MaxAttempts = 3;
    public const int SubjectMaxLength = 200;

    private NotificationDelivery()
    {
    }

    public Guid GymId { get; private set; }

    public Guid NotificationId { get; private set; }

    public DeliveryChannel Channel { get; private set; }

    public DeliveryStatus Status { get; private set; } = DeliveryStatus.Pending;

    public int Attempts { get; private set; }

    /// <summary>No se reintenta antes de esta hora (espera creciente tras cada fallo).</summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public string ToAddress { get; private set; } = string.Empty;

    public string Subject { get; private set; } = string.Empty;

    public string HtmlBody { get; private set; } = string.Empty;

    public string TextBody { get; private set; } = string.Empty;

    public static NotificationDelivery CreateEmail(
        Guid gymId, Guid notificationId, string toAddress, string subject, string htmlBody, string textBody)
    {
        if (gymId == Guid.Empty || notificationId == Guid.Empty)
        {
            throw new ValidationDomainException("La entrega necesita gym y aviso.");
        }

        if (string.IsNullOrWhiteSpace(toAddress))
        {
            throw new ValidationDomainException("El correo necesita un destinatario.");
        }

        return new NotificationDelivery
        {
            GymId = gymId,
            NotificationId = notificationId,
            Channel = DeliveryChannel.Email,
            ToAddress = toAddress.Trim(),
            Subject = subject.Length > SubjectMaxLength ? subject[..SubjectMaxLength] : subject,
            HtmlBody = htmlBody,
            TextBody = textBody,
        };
    }

    /// <summary>Correo que no se envía porque el miembro apagó ese canal para la categoría (N4). Sin cuerpo.</summary>
    public static NotificationDelivery CreateSkippedEmail(
        Guid gymId, Guid notificationId, string toAddress, string subject)
    {
        var delivery = CreateEmail(gymId, notificationId, toAddress, subject, string.Empty, string.Empty);
        delivery.Status = DeliveryStatus.Skipped;
        return delivery;
    }

    public void MarkSent(DateTimeOffset now)
    {
        Status = DeliveryStatus.Sent;
        SentAt = now;
        LastError = null;
        NextAttemptAt = null;
        Attempts++;
    }

    /// <summary>Al tercer fallo queda <see cref="DeliveryStatus.Failed"/>; antes, espera 2^intentos minutos.</summary>
    public void MarkAttemptFailed(string error, DateTimeOffset now)
    {
        Attempts++;
        LastError = error.Length > 500 ? error[..500] : error;
        if (Attempts >= MaxAttempts)
        {
            Status = DeliveryStatus.Failed;
            NextAttemptAt = null;
        }
        else
        {
            NextAttemptAt = now.AddMinutes(Math.Pow(2, Attempts));
        }
    }
}
