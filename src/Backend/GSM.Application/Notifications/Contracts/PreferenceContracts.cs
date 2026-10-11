namespace GSM.Application.Notifications.Contracts;

/// <summary>
/// Preferencia de una categoría. <paramref name="Category"/> viaja como texto (Membership, Classes, Progress,
/// News, Promotions): un enum suelto en el cuerpo de una respuesta sale mal del generador del cliente.
/// </summary>
public sealed record NotificationPreferenceItem(string Category, string Label, bool Push, bool Email);

public sealed record NotificationPreferencesResponse(IReadOnlyList<NotificationPreferenceItem> Items);

public sealed record NotificationPreferenceUpdate(string Category, bool Push, bool Email);

/// <summary>Las categorías que no vengan en la lista conservan su valor.</summary>
public sealed record UpdateNotificationPreferencesRequest(IReadOnlyList<NotificationPreferenceUpdate> Items);

/// <summary>Horario sin avisos del gym: horas locales (zona del gym).</summary>
public sealed record NotificationSettingsResponse(bool QuietHoursEnabled, TimeOnly QuietStart, TimeOnly QuietEnd);

public sealed record UpdateNotificationSettingsRequest(bool QuietHoursEnabled, TimeOnly QuietStart, TimeOnly QuietEnd);
