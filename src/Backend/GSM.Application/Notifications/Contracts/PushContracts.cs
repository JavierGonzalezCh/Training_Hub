namespace GSM.Application.Notifications.Contracts;

/// <summary>
/// Registro de un dispositivo para push. <paramref name="Platform"/> viaja como texto (<c>android</c>,
/// <c>web_member</c>, <c>web_panel</c>): un enum suelto en el cuerpo de una respuesta sale mal del
/// generador del cliente.
/// </summary>
public sealed record RegisterPushDeviceRequest(string Token, string Platform);

/// <summary>Baja del dispositivo (al cerrar sesión o al retirar el permiso). Idempotente.</summary>
public sealed record UnregisterPushDeviceRequest(string Token);

public sealed record PushDeviceResponse(Guid Id, string Platform);

/// <summary>
/// Datos públicos de Firebase para que la app web obtenga su token. Con <see cref="Enabled"/> en
/// <c>false</c> (Firebase sin configurar) la app no ofrece activar los avisos.
/// </summary>
public sealed record PushConfigResponse(
    bool Enabled,
    string? ProjectId,
    string? ApiKey,
    string? AppId,
    string? MessagingSenderId,
    string? VapidKey);

/// <summary>Un aviso al staff que se puede encender o apagar en el teléfono.</summary>
public sealed record StaffPushPreferenceItem(string Type, string Label, bool Push);

public sealed record StaffPushPreferencesResponse(IReadOnlyList<StaffPushPreferenceItem> Items);

public sealed record StaffPushPreferenceUpdate(string Type, bool Push);

public sealed record UpdateStaffPushPreferencesRequest(IReadOnlyList<StaffPushPreferenceUpdate> Items);
