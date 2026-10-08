using GSM.Application.Common.Events;

namespace GSM.Application.Communications.Events;

/// <summary>
/// A una membresía le queda poco para vencer (dentro de la ventana de recordatorio del gym). La
/// publica la tarea periódica en cada ejecución; Notifications deduplica por membresía.
/// </summary>
public sealed record MembershipReminderDue(
    Guid GymId,
    Guid MemberId,
    Guid MembershipId,
    string PlanName,
    DateOnly EndDate,
    int DaysLeft) : IDomainEvent;
