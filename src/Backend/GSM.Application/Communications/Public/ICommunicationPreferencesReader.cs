namespace GSM.Application.Communications.Public;

/// <summary>
/// Lectura que Comunicaciones expone a Notifications mientras las preferencias de correo siguen siendo
/// suyas (el interruptor <c>EmailOptOut</c> y el enlace de baja). En N4 las preferencias pasan a Notifications.
/// </summary>
public interface ICommunicationPreferencesReader
{
    /// <summary>De <paramref name="memberIds"/>, los que se dieron de baja del correo.</summary>
    Task<IReadOnlySet<Guid>> GetEmailOptedOutAsync(
        IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken = default);

    /// <summary>Enlace de baja firmado que va al pie de los correos del gym.</summary>
    string BuildUnsubscribeUrl(Guid memberId, Guid gymId);
}
