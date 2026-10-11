namespace GSM.Application.Communications.Contracts;

/// <summary>Correos de un anuncio enviado por email.</summary>
public sealed record EmailDelivery(int Pending, int Sent, int Failed);

public sealed record CommunicationsSettingsResponse(int ReminderDaysBefore);

public sealed record UpdateCommunicationsSettingsRequest(int ReminderDaysBefore);

/// <summary>
/// Resumen de una ejecución de la tarea periódica: cuántos recordatorios de vencimiento se publicaron
/// como evento (Notifications crea los avisos, sin duplicar) y cuántos avisos viejos se purgaron.
/// </summary>
public sealed record CommunicationsJobResult(int RemindersPublished, int NotificationsPurged);

/// <summary>Destinatarios de un anuncio por email (para avisar en el editor antes de publicar).</summary>
public sealed record EmailAudienceResponse(int Recipients);
