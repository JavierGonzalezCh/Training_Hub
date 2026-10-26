using GSM.Application.Achievements.Public;
using GSM.Domain.Attendance;

namespace GSM.Application.Attendance.Contracts;

/// <summary>
/// Staff/Owner: `MemberId` (registro manual). Miembro: `GymCode`, el código del QR del gym.
/// `AuthorizationReason` es el motivo de "Dejar pasar": solo el staff, y solo cuando la membresía no
/// deja entrar (vencida fuera de la gracia, sin plan o congelada). Sin motivo → 422.
/// `Latitude`/`Longitude`: ubicación del teléfono del miembro (A6), solo con el QR fijo y si el gym tiene
/// coordenadas. No se guardan: el servidor calcula la distancia y guarda solo el resultado (`LocationCheck`).
/// </summary>
public sealed record CheckInRequest(
    Guid? MemberId, string? GymCode, string? AuthorizationReason = null, double? Latitude = null, double? Longitude = null);

/// <summary>
/// ¿Debe la app pedir la ubicación al escanear? Solo con el QR fijo y si el gym configuró sus coordenadas (A6).
/// </summary>
public sealed record CheckinLocationPolicyResponse(bool LocationRequired);

/// <param name="Notice">Letrero de §5.3 (por vencer o en gracia). Nulo si no hay nada que avisar.</param>
/// <param name="DaysLeft">Días que le quedan cubiertos (0 = vence hoy). Nulo en gracia o sin cobertura.</param>
/// <param name="Authorized">La entrada fue autorizada por recepción fuera de la gracia.</param>
/// <param name="EntriesLeft">Entradas que le quedan en la tiquetera después de esta entrada (MS5). Nulo si no lo cubre una tiquetera.</param>
/// <param name="EntriesTotal">Entradas con que empezó la tiquetera.</param>
/// <param name="MissingWaiver">El miembro no tiene la exoneración de responsabilidad aceptada (recepción debe pedirla). Sin más detalle.</param>
public sealed record CheckInResponse(
    Guid AttendanceId,
    Guid MemberId,
    string MemberName,
    DateTimeOffset CheckedInAt,
    string Method,
    bool MembershipExpired,
    IReadOnlyList<UnlockedAchievement> UnlockedAchievements,
    DateOnly? MembershipEndsOn,
    int? GraceDaysLeft,
    string? Notice = null,
    int? DaysLeft = null,
    bool Authorized = false,
    bool MissingWaiver = false,
    int? EntriesLeft = null,
    int? EntriesTotal = null,
    string? LocationCheck = null,
    bool CountsForProgress = true,
    string? LocationNotice = null);

public sealed record CheckinAttemptItem(
    Guid Id,
    Guid MemberId,
    string MemberName,
    DateTimeOffset AttemptedAt,
    int AttemptCount,
    string Reason,
    DateOnly? MembershipEndedOn,
    DateTimeOffset? AcknowledgedAt);

public sealed record ListCheckinAttemptsQuery
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public bool PendingOnly { get; init; }

    /// <summary>Filtra por motivo (snake_case: <c>already_checked_in</c>, <c>membership_expired</c>…). Vacío = todos.</summary>
    public string? Reason { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed record AttendanceListItem(
    Guid Id,
    Guid MemberId,
    string MemberName,
    DateTimeOffset CheckedInAt,
    DateOnly AttendanceDate,
    string Method,
    bool Authorized = false,
    Guid? AuthorizedBy = null,
    string? AuthorizedByName = null,
    string? AuthorizationReason = null,
    bool Voided = false,
    DateTimeOffset? VoidedAt = null,
    Guid? VoidedBy = null,
    string? VoidedByName = null,
    string? VoidReason = null,
    string? LocationCheck = null,
    bool CountsForProgress = true);

public sealed record VoidCheckInRequest(string Reason);

public sealed record PeakHourBucket(int Hour, int Count);

public sealed record ListAttendanceQuery
{
    public Guid? MemberId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }

    /// <summary>Solo las entradas que recepción dejó pasar fuera de la gracia ("Dejar pasar").</summary>
    public bool AuthorizedOnly { get; init; }

    /// <summary>Solo las entradas por documento en el kiosco (A4).</summary>
    public bool DocumentOnly { get; init; }

    /// <summary>Incluye también las entradas anuladas (con su motivo). Por defecto no salen.</summary>
    public bool IncludeVoided { get; init; }

    /// <summary>Solo las entradas con QR fijo fuera del radio o sin ubicación: no cuentan para logros ni racha (A6).</summary>
    public bool NotCountedOnly { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

/// <summary>La gracia ya no es de Attendance: vive en los ajustes de membresías (`/api/membership-settings`).</summary>
/// <param name="ByDocument">La entrada con documento en el kiosco está encendida (A4; apagada por defecto).</param>
/// <param name="AssumedStayMinutes">Permanencia asumida para estimar la ocupación (A5; 120 por defecto).</param>
/// <param name="RemoteTraining">Los miembros entrenan fuera de la sede (T4): sin horario de apertura para el conteo por entrenamiento, sin aviso de riesgo y sin aforo de esas entradas.</param>
/// <param name="TrainingEntryTrigger"><c>Start</c> (al tocar «Iniciar rutina») o <c>FirstLog</c> (al registrar el primer ejercicio). Solo aplica sin registro en la puerta.</param>
public sealed record CheckinSettingsResponse(
    string Mode, bool ByDocument, int AssumedStayMinutes, bool RemoteTraining = false, string TrainingEntryTrigger = "Start");

/// <param name="ByDocument">Enciende o apaga la entrada con documento en el kiosco. Nulo = no cambia.</param>
/// <param name="AssumedStayMinutes">Permanencia asumida en minutos (15 a 480). Nulo = no cambia.</param>
/// <param name="RemoteTraining">Nulo = no cambia.</param>
/// <param name="TrainingEntryTrigger"><c>Start</c> o <c>FirstLog</c>. Nulo = no cambia.</param>
public sealed record UpdateCheckinSettingsRequest(
    string Mode, bool? ByDocument = null, int? AssumedStayMinutes = null,
    bool? RemoteTraining = null, string? TrainingEntryTrigger = null);

/// <summary>
/// Cuánta gente hay en el gym ahora (estimación: entradas no anuladas de las últimas <c>AssumedStayMinutes</c>,
/// una vez por persona). <c>Capacity</c>, <c>Percent</c> y <c>Level</c> son nulos si el gym no configuró aforo.
/// <c>Level</c>: <c>calm</c> (&lt; 40 %), <c>moderate</c> (40-75 %) o <c>full</c> (&gt; 75 %).
/// </summary>
public sealed record OccupancyResponse(int PeopleInside, int? Capacity, int? Percent, string? Level, int AssumedStayMinutes);

/// <summary>Lo que ve el miembro: solo el nivel (<c>calm</c>, <c>moderate</c>, <c>full</c>), sin número. Nulo si el gym no tiene aforo.</summary>
public sealed record MemberOccupancyResponse(string? Level);

/// <summary>
/// Código vigente del gym. <c>ExpiresAtUtc</c> solo en modo rotativo. <c>ByDocument</c>: el kiosco muestra el teclado del
/// documento. <c>QrDisabled</c> (A4-2): el check-in con QR está apagado y solo queda el documento; <c>Code</c> viene vacío
/// y el kiosco no dibuja el QR.
/// </summary>
public sealed record KioskCodeResponse(string Mode, string Code, DateTimeOffset? ExpiresAtUtc, bool ByDocument, bool QrDisabled);

/// <summary>Documento digitado en el teclado numérico del kiosco (solo dígitos, 4 a 20).</summary>
public sealed record KioskDocumentEntryRequest(string Document);

/// <summary>
/// Lo único que el kiosco muestra al entrar: el nombre de pila y la foto, nada más (sin apellido, fechas ni
/// montos). <c>ExpiringSoon</c>: por vencer o en gracia → "Tu plan vence pronto, pasa por recepción".
/// </summary>
public sealed record KioskDocumentEntryResponse(string FirstName, string? PhotoUrl, bool ExpiringSoon);

/// <summary>
/// Tarjeta de recepción (paso 1 de §4): lo que se ve del miembro antes de registrar nada. No cambia
/// datos. <c>RejectionReason</c> (snake_case) explica por qué no se puede registrar; nulo si se puede.
/// </summary>
/// <param name="Commercial"><c>no_plan</c>, <c>current</c>, <c>expiring</c>, <c>grace</c>, <c>expired</c> o <c>frozen</c>.</param>
/// <param name="Kind"><c>covered</c>, <c>expiring</c>, <c>grace</c> o <c>blocked</c>.</param>
/// <param name="RejectionReason"><c>gym_closed</c>, <c>member_inactive</c>, <c>already_checked_in</c>, <c>no_membership</c>, <c>membership_expired</c>, <c>membership_frozen</c> o <c>pass_exhausted</c> (tiquetera sin entradas).</param>
/// <param name="EntriesLeft">Entradas que le quedan en la tiquetera (MS5). Nulo si no lo cubre una tiquetera.</param>
/// <param name="EntriesTotal">Entradas con que empezó la tiquetera.</param>
/// <param name="CanRegister">Se puede pulsar "Registrar entrada" (abierto, activo, no entró hoy y cubierto o en gracia).</param>
/// <param name="CanLetThrough">Se puede "Dejar pasar" con motivo: bloqueado por la membresía, con el gym abierto y el miembro activo.</param>
/// <param name="CheckedInAt">Instante de la última entrada de hoy, si ya entró.</param>
/// <param name="EntriesToday">Entradas registradas hoy.</param>
/// <param name="MaxDailyEntries">Entradas por día que rigen hoy: 1 por defecto; más si el staff le habilitó entradas múltiples.</param>
/// <param name="MultipleEntries">Tiene entradas múltiples vigentes ("Entradas múltiples: 2 por día · lleva 1 hoy").</param>
/// <param name="NextEntryAt">Si aún le quedan entradas pero no pasaron 2 horas desde la última: desde cuándo puede volver a entrar.</param>
public sealed record ReceptionCardResponse(
    Guid MemberId,
    string MemberName,
    string? PhotoUrl,
    bool MemberActive,
    string Commercial,
    string Kind,
    DateOnly? EndsOn,
    int? DaysLeft,
    int? GraceDaysLeft,
    string? Notice,
    string? BlockMessage,
    bool AlreadyCheckedInToday,
    DateTimeOffset? CheckedInAt,
    bool MissingWaiver,
    bool GymClosed,
    string? GymClosedMessage,
    bool CanRegister,
    bool CanLetThrough,
    string? RejectionReason,
    int EntriesToday,
    int MaxDailyEntries,
    bool MultipleEntries,
    DateTimeOffset? NextEntryAt,
    bool BirthdayToday,
    int? EntriesLeft,
    int? EntriesTotal,
    IReadOnlyList<ReceptionRewardResponse> PendingRewards);

/// <summary>Premio de un logro que el miembro puede reclamar en recepción (L5). <c>ExpiresAt</c> nulo = no vence.</summary>
public sealed record ReceptionRewardResponse(
    Guid AchievementId,
    string AchievementName,
    string RewardDescription,
    DateTimeOffset? ExpiresAt);

/// <summary>Habilita (o cambia) las entradas múltiples de un miembro. Máximo de 2 a 5; motivo obligatorio; vencimiento opcional.</summary>
public sealed record SetEntryRuleRequest(int MaxDailyEntries, string Reason, DateOnly? ValidUntil);

/// <summary>
/// Regla de entradas múltiples de un miembro. <c>Enabled</c> es falso si no hay regla o ya venció
/// (<c>Expired</c>); en ese caso rige una entrada por día.
/// </summary>
public sealed record EntryRuleResponse(
    Guid MemberId,
    bool Enabled,
    bool Expired,
    int? MaxDailyEntries,
    string? Reason,
    DateOnly? ValidUntil,
    Guid? GrantedBy,
    string? GrantedByName,
    DateTimeOffset? GrantedAt);
