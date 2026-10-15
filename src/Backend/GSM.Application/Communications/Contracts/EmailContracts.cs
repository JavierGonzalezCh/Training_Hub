namespace GSM.Application.Communications.Contracts;

/// <summary>Correos de un anuncio enviado por email.</summary>
public sealed record EmailDelivery(int Pending, int Sent, int Failed);

public sealed record CommunicationsSettingsResponse(int ReminderDaysBefore);

/// <summary>
/// Felicitación de cumpleaños del gym (§F). <c>Message</c> es el texto vigente (el del gym o el de por defecto);
/// <c>IsDefaultMessage</c> avisa cuál es. Variables: <c>{nombre}</c> y <c>{gym}</c>.
/// </summary>
public sealed record BirthdaySettingsResponse(
    bool Enabled, string Message, string DefaultMessage, bool IsDefaultMessage, IReadOnlyList<string> Variables);

/// <summary>Enciende o apaga la felicitación y fija su texto. Un texto vacío vuelve al de por defecto.</summary>
public sealed record UpdateBirthdaySettingsRequest(bool Enabled, string? Message);

public sealed record UpdateCommunicationsSettingsRequest(int ReminderDaysBefore);

/// <summary>
/// Resumen de una ejecución de la tarea periódica: cuántos recordatorios de vencimiento se publicaron
/// como evento (Notifications crea los avisos, sin duplicar) y cuántos avisos viejos se purgaron.
/// </summary>
public sealed record CommunicationsJobResult(int RemindersPublished, int NotificationsPurged, int BirthdaysPublished);

/// <summary>Lo que publicó la tarea: recordatorios de vencimiento y felicitaciones de cumpleaños.</summary>
public sealed record CommunicationsRunOutcome(int Reminders, int Birthdays);

/// <summary>Destinatarios de un anuncio por email (para avisar en el editor antes de publicar).</summary>
public sealed record EmailAudienceResponse(int Recipients);
