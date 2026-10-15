using GSM.Domain.Common.Exceptions;
using GSM.Domain.Memberships;

namespace GSM.Application.Memberships;

/// <summary>Inicio calculado por el sistema para una membresía nueva y la situación que lo determinó.</summary>
public sealed record RenewalPlan(RenewalKind Kind, DateOnly StartDate, DateOnly? PreviousEndDate);

/// <summary>
/// Regla de renovación (Memberships v2, §4.1). El inicio lo calcula el sistema según la situación
/// del miembro; el staff solo puede cambiarlo caso a caso, con motivo. Pura, como el calculador de estado.
/// </summary>
public static class RenewalPlanner
{
    public static RenewalPlan Plan(IEnumerable<MembershipSpan> spans, DateOnly today, int graceDays)
    {
        // Las canceladas no cuentan: su hueco queda libre y "la última fue cancelada" equivale a no tener.
        var live = spans
            .Where(s => s.Status is MembershipStatus.Active or MembershipStatus.Paused)
            .ToList();

        if (live.Count == 0)
        {
            return new RenewalPlan(RenewalKind.New, today, null);
        }

        var lastEnd = live.Max(s => s.EndDate);
        if (lastEnd >= today || live.Any(s => s.IsFrozenOn(today)))
        {
            return new RenewalPlan(RenewalKind.Early, lastEnd.AddDays(1), lastEnd);
        }

        return today <= lastEnd.AddDays(graceDays)
            ? new RenewalPlan(RenewalKind.InGrace, lastEnd.AddDays(1), lastEnd)
            : new RenewalPlan(RenewalKind.AfterGrace, today, lastEnd);
    }

    /// <summary>
    /// Aplica la fecha elegida por el staff sobre el cálculo del sistema. Sin fecha (o igual a la
    /// calculada) no hay cambio. En una membresía <c>new</c> la fecha es libre (recepción elige el
    /// día de inicio); en las demás exige motivo y no puede solaparse con la anterior.
    /// </summary>
    public static (DateOnly StartDate, string? Reason) ApplyOverride(
        RenewalPlan plan, DateOnly? requestedStart, string? reason, bool requireReason = true)
    {
        if (requestedStart is not { } requested || requested == plan.StartDate)
        {
            return (plan.StartDate, null);
        }

        if (plan.PreviousEndDate is { } previousEnd && requested <= previousEnd)
        {
            throw new ValidationDomainException(
                $"La fecha de inicio no puede cruzarse con la membresía anterior, que termina el {CommercialStatusCalculator.ShortDate(previousEnd)}.");
        }

        var trimmed = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (plan.Kind == RenewalKind.New || !requireReason)
        {
            return (requested, trimmed);
        }

        if (trimmed is null || trimmed.Length is < 3 or > 500)
        {
            throw new ValidationDomainException(
                "Para cambiar la fecha de inicio que calcula el sistema indica un motivo (3 a 500 caracteres).");
        }

        return (requested, trimmed);
    }
}
