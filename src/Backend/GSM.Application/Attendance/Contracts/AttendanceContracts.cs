using GSM.Application.Achievements.Public;
using GSM.Domain.Attendance;

namespace GSM.Application.Attendance.Contracts;

/// <summary>Check-in por QR o por memberId. Un `member` autenticado ignora ambos: se registra a sí mismo.</summary>
public sealed record CheckInRequest(string? QrCode, Guid? MemberId, AttendanceMethod? Method);

public sealed record CheckInResponse(
    Guid AttendanceId,
    Guid MemberId,
    string MemberName,
    DateTimeOffset CheckedInAt,
    string Method,
    bool AlreadyCheckedInToday,
    bool MembershipExpired,
    IReadOnlyList<UnlockedAchievement> UnlockedAchievements);

public sealed record AttendanceListItem(
    Guid Id,
    Guid MemberId,
    string MemberName,
    DateTimeOffset CheckedInAt,
    DateOnly AttendanceDate,
    string Method);

public sealed record MemberStreakResponse(
    Guid MemberId,
    int CurrentStreak,
    int TotalVisits,
    DateOnly? LastVisit);

public sealed record PeakHourBucket(int Hour, int Count);

public sealed record ListAttendanceQuery
{
    public Guid? MemberId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
