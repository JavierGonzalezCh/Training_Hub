using GSM.Application.Common.Events;

namespace GSM.Application.Communications.Events;

/// <summary>Cuál de los tres recordatorios de vencimiento es (Comunicaciones v2, §H).</summary>
public enum MembershipReminderKind
{
    /// <summary>N días antes del vencimiento (<c>ReminderDaysBefore</c>).</summary>
    DaysBefore,

    /// <summary>El día que vence: "Tu plan vence hoy".</summary>
    ExpiresToday,

    /// <summary>El último día de la gracia: "Hoy es tu último día para renovar sin perder tu fecha".</summary>
    LastGraceDay,
}

/// <summary>
/// Toca avisar del vencimiento de una membresía. La publica la tarea periódica en cada ejecución;
/// Notifications deduplica por miembro, tipo y referencia (la referencia depende de <see cref="Kind"/>, así
/// cada recordatorio sale una sola vez por membresía).
/// </summary>
/// <param name="DaysLeft">Días hasta <c>EndDate</c> (0 = vence hoy; negativo en el último día de gracia).</param>
public sealed record MembershipReminderDue(
    Guid GymId,
    Guid MemberId,
    Guid MembershipId,
    string PlanName,
    DateOnly EndDate,
    int DaysLeft,
    MembershipReminderKind Kind) : IDomainEvent;
