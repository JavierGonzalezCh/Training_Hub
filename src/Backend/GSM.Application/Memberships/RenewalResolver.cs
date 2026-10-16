using GSM.Application.Common.Abstractions;
using GSM.Application.Memberships.Abstractions;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Memberships;

namespace GSM.Application.Memberships;

/// <summary>Resultado de resolver el inicio de una membresía nueva: lo calculado y lo que se usará.</summary>
public sealed record ResolvedRenewal(
    RenewalPlan Plan, DateOnly StartDate, string? StartOverrideReason, Guid? PreviousMembershipId,
    bool IsFirstMembership, decimal GymEnrollmentFee, int? InheritedCycleDay = null);

/// <summary>
/// Calcula el inicio de una membresía nueva con la gracia del gym (§4.1). La comparten "asignar" y la
/// vista previa para que lo que se muestra antes de confirmar sea exactamente lo que se crea.
/// </summary>
public sealed class RenewalResolver(IMembershipRepository memberships, IClock clock)
{
    public async Task<ResolvedRenewal> ResolveAsync(
        Guid gymId, Guid memberId, DateOnly? requestedStart, string? reason, CancellationToken cancellationToken,
        bool requireReason = true, MembershipPlan? newPlan = null)
    {
        var today = clock.Today;
        var settings = await memberships.GetSettingsAsync(gymId, cancellationToken)
            ?? MembershipSettings.Default(gymId);

        var history = await memberships.GetHistoryAsync(memberId, cancellationToken);

        // Tiqueteras (MS5, §4.1): siempre empiezan el día en que se compran, sin gracia ni encadenar. La fecha
        // no se cambia (ya no se ignora en silencio: se rechaza si pide otra).
        if (newPlan is { IsPass: true })
        {
            if (requestedStart is { } asked && asked != today)
            {
                throw new ValidationDomainException("Una tiquetera siempre empieza el día en que se compra.");
            }

            return new ResolvedRenewal(
                new RenewalPlan(RenewalKind.New, today, null), today, null, null,
                history.Count == 0, settings.EnrollmentFee, null);
        }

        // Una tiquetera del historial no entra en la cadena de renovación de un plan por tiempo: no tiene
        // gracia ni ciclo, y sus entradas sobrantes se pierden al vencer.
        var timeHistory = history.Where(m => !m.IsPass).ToList();
        var freezes = await memberships.GetFreezesAsync(timeHistory.Select(m => m.Id).ToList(), cancellationToken);
        var plan = RenewalPlanner.Plan(
            timeHistory.Select(m => FreezeSpans.SpanOf(m, freezes)), today, settings.GraceDays);

        var previous = timeHistory
            .Where(m => m.Status is MembershipStatus.Active or MembershipStatus.Paused)
            .OrderByDescending(m => m.EndDate)
            .FirstOrDefault();
        var previousId = previous?.Id;

        var (start, overrideReason) = RenewalPlanner.ApplyOverride(plan, requestedStart, reason, requireReason);

        // MS3: el día del ciclo de un plan en meses solo se hereda de una membresía en meses que
        // termina justo antes de la nueva (encadenada). Con cualquier hueco o cambio, arranca de cero.
        int? inheritedCycleDay = previous is { CycleDay: { } cycle } && start == previous.EndDate.AddDays(1)
            ? cycle
            : null;
        // La matrícula aplica a quien nunca tuvo una membresía en el gym (§4.1); las canceladas cuentan como historial.
        return new ResolvedRenewal(
            plan, start, overrideReason, plan.Kind == RenewalKind.New ? null : previousId,
            history.Count == 0, settings.EnrollmentFee, inheritedCycleDay);
    }
}
