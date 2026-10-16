using GSM.Domain.Notifications;

namespace GSM.Application.Notifications.Abstractions;

/// <summary>
/// Envío de push a un dispositivo. Implementado sobre Firebase Cloud Messaging (API HTTP v1); sin
/// credenciales de Firebase se registra una implementación que omite el envío y lo deja en el log
/// (mismo criterio que el correo sin Resend).
/// </summary>
public interface IPushSender
{
    /// <summary>¿Hay un proveedor real configurado? Con <c>false</c> el envío se omite.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Envía el mensaje. Un fallo transitorio (red, 5xx, cuota) lanza una excepción y el dispatcher
    /// reintenta; un token que el proveedor ya no reconoce se informa con <see cref="PushSendResult.InvalidToken"/>.
    /// </summary>
    Task<PushSendResult> SendAsync(PushMessage message, CancellationToken cancellationToken = default);
}

public enum PushSendResult
{
    Sent,

    /// <summary>El token ya no sirve (app desinstalada, permiso retirado): el dispositivo se revoca solo.</summary>
    InvalidToken,

    /// <summary>No hay proveedor configurado: nada se envió.</summary>
    NotConfigured,
}

/// <param name="Link">Ruta interna que abre la app al tocar el aviso.</param>
public sealed record PushMessage(
    string Token,
    PushPlatform Platform,
    string Title,
    string Body,
    string? Link,
    Guid NotificationId,
    Guid GymId);

public interface IPushDeviceRepository
{
    /// <summary>El dispositivo con ese token, revocado o no (con seguimiento). Sin filtro de gym: es de la persona.</summary>
    Task<PushDevice?> FindByTokenAsync(string token, CancellationToken cancellationToken = default);

    void Add(PushDevice device);

    /// <summary>Dispositivos activos de la persona en esas plataformas (con seguimiento).</summary>
    Task<IReadOnlyList<PushDevice>> GetActiveForUserAsync(
        Guid userId, IReadOnlyCollection<PushPlatform> platforms, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, PushDevice>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>Borra todos los dispositivos de la persona (su cuenta se eliminó). Escribe al instante; idempotente.</summary>
    Task DeleteForUserAsync(Guid userId, CancellationToken cancellationToken = default);
}

public interface IStaffNotificationPreferenceRepository
{
    /// <summary>Las filas de la persona en el gym (con seguimiento, para actualizarlas).</summary>
    Task<IReadOnlyList<StaffNotificationPreference>> GetForUserAsync(
        Guid gymId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>De varias personas en el gym: las filas que existen, sin seguimiento. Sin fila vale el valor por defecto.</summary>
    Task<IReadOnlyDictionary<(Guid UserId, NotificationType Type), bool>> GetForUsersAsync(
        Guid gymId, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default);

    /// <summary>Quiénes pidieron explícitamente el push de ese tipo de aviso en el gym (avisos de acción del gym).</summary>
    Task<IReadOnlyList<Guid>> GetUserIdsWithPushAsync(
        Guid gymId, NotificationType type, CancellationToken cancellationToken = default);

    void Add(StaffNotificationPreference preference);
}
