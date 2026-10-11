using GSM.Application.Achievements.Public;
using GSM.Domain.Attendance;

namespace GSM.Application.Attendance.Contracts;

/// <summary>
/// Staff/Owner: `MemberId` (registro manual). Miembro: `GymCode`, el código del QR del gym.
/// `AuthorizationReason` es el motivo de "Dejar pasar": solo el staff, y solo cuando la membresía no
/// deja entrar (vencida fuera de la gracia, sin plan o congelada). Sin motivo → 422.
/// </summary>
public sealed record CheckInRequest(Guid? MemberId, string? GymCode, string? AuthorizationReason = null);

/// <param name="Notice">Letrero de §5.3 (por vencer o en gracia). Nulo si no hay nada que avisar.</param>
/// <param name="DaysLeft">Días que le quedan cubiertos (0 = vence hoy). Nulo en gracia o sin cobertura.</param>
/// <param name="Authorized">La entrada fue autorizada por recepción fuera de la gracia.</param>
/// <param name="MissingWaiver">El miembro no tiene la exoneración de responsabilidad aceptada (recepción debe pedirla). Sin más detalle.</param>
public sealed record CheckInResponse(
    Guid AttendanceId,
    Guid MemberId,
    string MemberName,
    DateTimeOffset CheckedInAt,
    string Method,
    bool AlreadyCheckedInToday,
    bool MembershipExpired,
    IReadOnlyList<UnlockedAchievement> UnlockedAchievements,
    DateOnly? MembershipEndsOn,
    int? GraceDaysLeft,
    string? Notice = null,
    int? DaysLeft = null,
    bool Authorized = false,
    bool MissingWaiver = false);

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
    string? AuthorizationReason = null);

public sealed record PeakHourBucket(int Hour, int Count);

public sealed record ListAttendanceQuery
{
    public Guid? MemberId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }

    /// <summary>Solo las entradas que recepción dejó pasar fuera de la gracia ("Dejar pasar").</summary>
    public bool AuthorizedOnly { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

/// <summary>La gracia ya no es de Attendance: vive en los ajustes de membresías (`/api/membership-settings`).</summary>
public sealed record CheckinSettingsResponse(string Mode);

public sealed record UpdateCheckinSettingsRequest(string Mode);

/// <summary>Código vigente del gym. <c>ExpiresAtUtc</c> solo en modo rotativo.</summary>
public sealed record KioskCodeResponse(string Mode, string Code, DateTimeOffset? ExpiresAtUtc);
