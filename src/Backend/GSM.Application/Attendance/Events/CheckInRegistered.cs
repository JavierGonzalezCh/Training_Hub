using GSM.Application.Common.Events;

namespace GSM.Application.Attendance.Events;

/// <summary>
/// Se registró una entrada. <paramref name="AttendanceDate"/> es su día de negocio (en una clase marcada
/// después puede no ser hoy). Memberships (MS5) la usa para descontar la entrada de una tiquetera.
/// </summary>
public sealed record CheckInRegistered(
    Guid AttendanceId, Guid GymId, Guid MemberId, DateOnly AttendanceDate) : IDomainEvent;
