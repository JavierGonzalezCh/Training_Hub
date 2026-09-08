using GSM.Application.Common.Events;

namespace GSM.Application.Attendance.Events;

public sealed record CheckInRegistered(
    Guid AttendanceId, Guid GymId, Guid MemberId) : IDomainEvent;
