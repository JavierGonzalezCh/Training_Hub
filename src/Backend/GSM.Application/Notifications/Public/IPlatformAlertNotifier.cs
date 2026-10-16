namespace GSM.Application.Notifications.Public;

/// <summary>
/// LG5: entrega de las alertas de la plataforma al SuperAdmin por correo y push. El SuperAdmin no pertenece a
/// ningún gym, así que estas alertas no pasan por la bandeja (<c>notifications.notifications</c> es de un gym):
/// Notifications solo pone el canal de salida. Lo consume el evaluador de alertas de Logs.
/// </summary>
public interface IPlatformAlertNotifier
{
    /// <summary>
    /// Entrega la alerta por los canales pedidos. Nunca lanza: un fallo de un canal vuelve como
    /// <see cref="PlatformAlertChannelResult.Failed"/> para que el evaluador lo reintente.
    /// </summary>
    Task<PlatformAlertDeliveryResult> DeliverAsync(
        PlatformAlertMessage message, bool sendEmail, bool sendPush, CancellationToken cancellationToken = default);
}

/// <param name="AlertId">Identifica la alerta en el push (va como id del aviso).</param>
/// <param name="Link">Ruta interna del panel que abre el push al tocarlo.</param>
public sealed record PlatformAlertMessage(Guid AlertId, string Title, string Body, string? Link);

public enum PlatformAlertChannelResult
{
    /// <summary>El canal no se pidió en este intento.</summary>
    NotAttempted,

    Sent,

    /// <summary>No hay a dónde enviar: sin SuperAdmin con correo, sin dispositivos o sin Firebase configurado.</summary>
    Skipped,

    Failed,
}

public sealed record PlatformAlertDeliveryResult(
    PlatformAlertChannelResult Email,
    PlatformAlertChannelResult Push,
    string? Error);
