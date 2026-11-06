using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Routines;

/// <summary>Por qué cambió lo que tocaba en una fecha (SP, docs/GSM-sesion-perdida.md).</summary>
public enum RoutineDayOverrideReason
{
    /// <summary>Hoy hizo la sesión perdida de ayer en lugar de la de hoy; el resto de la semana no cambia.</summary>
    Swap,

    /// <summary>Corrió la semana: las rutinas se corren en orden, los descansos quedan fijos.</summary>
    Reorganize,
}

/// <summary>
/// Ajuste del calendario de un miembro en una fecha: ese día cuenta con <see cref="PerformedDayId"/> en lugar de
/// <see cref="PlannedDayId"/>. Lo consulta <c>RoutineCalendar</c>, así que la racha y el cumplimiento siguen diciendo
/// lo mismo de cada fecha. Único por <c>(member_id, date)</c>.
/// </summary>
public sealed class RoutineDayOverride : BaseEntity, ITenantEntity
{
    private RoutineDayOverride()
    {
    }

    public Guid GymId { get; private set; }

    public Guid MemberId { get; private set; }

    public Guid RoutineId { get; private set; }

    /// <summary>Día de negocio, no UTC.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>El día de la rutina que tocaba según el plan.</summary>
    public Guid PlannedDayId { get; private set; }

    /// <summary>El día de la rutina que cuenta en esa fecha.</summary>
    public Guid PerformedDayId { get; private set; }

    public RoutineDayOverrideReason Reason { get; private set; }

    public static RoutineDayOverride Create(
        Guid gymId, Guid memberId, Guid routineId, DateOnly date, Guid plannedDayId, Guid performedDayId,
        RoutineDayOverrideReason reason)
    {
        if (gymId == Guid.Empty || memberId == Guid.Empty || routineId == Guid.Empty
            || plannedDayId == Guid.Empty || performedDayId == Guid.Empty)
        {
            throw new ValidationDomainException("Datos del cambio de día incompletos.");
        }

        if (plannedDayId == performedDayId)
        {
            throw new ValidationDomainException("El día que se hace debe ser distinto del que tocaba.");
        }

        return new RoutineDayOverride
        {
            GymId = gymId,
            MemberId = memberId,
            RoutineId = routineId,
            Date = date,
            PlannedDayId = plannedDayId,
            PerformedDayId = performedDayId,
            Reason = reason,
        };
    }
}
