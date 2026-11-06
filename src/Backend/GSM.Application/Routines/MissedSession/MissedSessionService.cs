using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms.Public;
using GSM.Application.Identity.Public;
using GSM.Application.Members.Public;
using GSM.Application.Routines.Abstractions;
using GSM.Application.Routines.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Routines;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Routines.MissedSession;

/// <summary>Lee lo necesario y arma la sesión perdida de ayer con <see cref="MissedSessionPlanner"/>.</summary>
public sealed class MissedSessionService(IAdherenceSource source, IIdentityReader identity)
{
    public async Task<MissedSessionPlan?> PlanAsync(Guid memberId, DateOnly today, CancellationToken cancellationToken)
    {
        var data = await source.LoadAsync(
            [memberId], today.AddDays(-1), MissedSessionPlanner.WeekEndOf(today), cancellationToken);
        if (data.Routines.Count == 0)
        {
            return null;
        }

        return MissedSessionPlanner.Detect(
            RoutineCalendar.Order(data.Routines), data.Logs, RoutineCalendar.ByDate(data.OverridesOrEmpty), today);
    }

    public async Task<MissedSessionView?> ViewAsync(
        Guid memberId, DateOnly today, Guid assignedBy, bool selfAssigned, CancellationToken cancellationToken)
    {
        var plan = await PlanAsync(memberId, today, cancellationToken);
        if (plan is null)
        {
            return null;
        }

        string? trainer = null;
        if (!selfAssigned && assignedBy != Guid.Empty)
        {
            var user = await identity.GetUserAsync(assignedBy, cancellationToken);
            trainer = user is { IsSuperAdmin: false } ? user.FullName : null;
        }

        return new MissedSessionView(
            plan.MissedOn,
            plan.MissedDay.Id,
            MissedSessionPlanner.NameOf(plan.MissedDay),
            plan.TodayIsRest,
            Slots(plan.Before),
            plan.Swap is { } swap ? Option(swap) : null,
            plan.Reorganize is { } reorganize ? Option(reorganize) : null,
            plan.Routine.Mode == ScheduleMode.Rotating,
            trainer);
    }

    private static MissedSessionOptionView Option(MissedSessionOption option)
        => new(Slots(option.Week), option.LostDayName, option.ShiftsCycle);

    private static List<WeekSlotView> Slots(IReadOnlyList<WeekSlot> week)
        => week.Select(s => new WeekSlotView(s.Date, IsoWeekDay.Of(s.Date), s.IsRest, s.DayId, s.DayName)).ToList();
}

/// <summary>
/// Hacer hoy la sesión perdida de ayer (reglas 4b y 5). Recalcula la misma cuenta que mostró la vista previa y guarda
/// sus cambios en una transacción; devuelve la rutina del miembro ya con el cambio.
/// </summary>
public sealed class RecoverMissedSessionHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    IMemberReader members,
    MissedSessionService missed,
    IRoutineDayOverrideRepository overrides,
    IUnitOfWork unitOfWork,
    GetMyRoutineHandler myRoutine,
    ILogger<RecoverMissedSessionHandler> logger)
{
    public async Task<MyRoutineResponse> HandleAsync(RecoverMissedSessionRequest request, CancellationToken cancellationToken)
    {
        var memberId = await RoutineMemberContext.ResolveAsync(currentUser, gyms, members, cancellationToken);
        var gymId = currentUser.RequireGymId();
        var today = clock.Today;

        var plan = await missed.PlanAsync(memberId, today, cancellationToken)
            ?? throw new ConflictException("No hay una sesión perdida de ayer para recuperar.");

        if (request.PerformedDayId != plan.MissedDay.Id)
        {
            throw new ValidationDomainException("Solo puedes recuperar la rutina que te faltó ayer.");
        }

        if (plan.TodayIsRest)
        {
            throw new ValidationDomainException(
                "Hoy es descanso: puedes hacer la rutina de ayer sin cambiar tu semana.");
        }

        var option = request.Reorganize ? plan.Reorganize! : plan.Swap!;
        var reason = !request.Reorganize ? RoutineDayOverrideReason.Swap
            : option.ShiftsCycle ? RoutineDayOverrideReason.Shift
            : RoutineDayOverrideReason.Reorganize;

        // Reemplaza los cambios que hubiera en las fechas que toca esta decisión. Correr el ciclo es una sola fila hoy:
        // vale para hoy y todo lo que sigue.
        var weekEnd = option.Week[^1].Date;
        IReadOnlyList<PlannedOverride> changes = option.ShiftsCycle
            ? [new PlannedOverride(today, plan.Before[0].PlannedDayId!.Value, plan.MissedDay.Id)]
            : MissedSessionPlanner.Changes(option);
        var touched = reason == RoutineDayOverrideReason.Reorganize ? option.Week.Select(s => s.Date).ToHashSet() : [today];
        foreach (var existing in await overrides.ListAsync(memberId, today, weekEnd, cancellationToken))
        {
            if (touched.Contains(existing.Date))
            {
                overrides.Remove(existing);
            }
        }

        foreach (var change in changes)
        {
            overrides.Add(RoutineDayOverride.Create(
                gymId, memberId, plan.Routine.Id, change.Date, change.PlannedDayId, change.PerformedDayId, reason));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Miembro {MemberId} recuperó la sesión del {MissedOn} ({Reason}, {Count} cambios)",
            memberId, plan.MissedOn, reason, changes.Count);

        return await myRoutine.HandleAsync(cancellationToken);
    }
}
