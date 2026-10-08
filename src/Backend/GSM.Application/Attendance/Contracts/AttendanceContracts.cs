using GSM.Application.Achievements.Public;
using GSM.Domain.Attendance;

namespace GSM.Application.Attendance.Contracts;

/// <summary>Staff/Owner: `MemberId` (registro manual). Miembro: `GymCode`, el código del QR del gym.</summary>
public sealed record CheckInRequest(Guid? MemberId, string? GymCode);

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
    int? GraceDaysLeft);

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
    string Method);

public sealed record PeakHourBucket(int Hour, int Count);

public sealed record ListAttendanceQuery
{
    public Guid? MemberId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed record CheckinSettingsResponse(string Mode, int GraceDays);

public sealed record UpdateCheckinSettingsRequest(string Mode, int GraceDays);

/// <summary>Código vigente del gym. <c>ExpiresAtUtc</c> solo en modo rotativo.</summary>
public sealed record KioskCodeResponse(string Mode, string Code, DateTimeOffset? ExpiresAtUtc);
