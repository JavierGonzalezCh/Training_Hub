using GSM.Application.Common.Events;
using GSM.Domain.Attendance;

namespace GSM.Application.Attendance.Events;

/// <summary>Disparador reservado para push o email al gym; hoy no tiene consumidor.</summary>
public sealed record CheckInRejected(
    Guid AttemptId, Guid GymId, Guid MemberId, CheckinRejectionReason Reason) : IDomainEvent;
