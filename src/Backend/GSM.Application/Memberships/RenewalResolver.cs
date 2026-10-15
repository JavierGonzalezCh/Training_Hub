using GSM.Application.Common.Abstractions;
using GSM.Application.Memberships.Abstractions;
using GSM.Domain.Memberships;

namespace GSM.Application.Memberships;

/// <summary>Resultado de resolver el inicio de una membresía nueva: lo calculado y lo que se usará.</summary>
public sealed record ResolvedRenewal(
    RenewalPlan Plan, DateOnly StartDate, string? StartOverrideReason, Guid? PreviousMembershipId,
    bool IsFirstMembership, decimal GymEnrollmentFee);

/// <summary>
/// Calcula el inicio de una membresía nueva con la gracia del gym (§4.1). La comparten "asignar" y la
/// vista previa para que lo que se muestra antes de confirmar sea exactamente lo que se crea.
/// </summary>
public sealed class RenewalResolver(IMembershipRepository memberships, IClock clock)
{
    public async Task<ResolvedRenewal> ResolveAsync(
        Guid gymId, Guid memberId, DateOnly? requestedStart, string? reason, CancellationToken cancellationToken,
        bool requireReason = true)
    {
        var today = clock.Today;
        var settings = await memberships.GetSettingsAsync(gymId, cancellationToken)
            ?? MembershipSettings.Default(gymId);

        var history = await memberships.GetHistoryAsync(memberId, cancellationToken);
        var plan = RenewalPlanner.Plan(
            history.Select(m => new MembershipSpan(m.Status, m.StartDate, m.EndDate)), today, settings.GraceDays);

        var previousId = history
            .Where(m => m.Status is MembershipStatus.Active or MembershipStatus.Paused)
            .OrderByDescending(m => m.EndDate)
            .Select(m => (Guid?)m.Id)
            .FirstOrDefault();

        var (start, overrideReason) = RenewalPlanner.ApplyOverride(plan, requestedStart, reason, requireReason);
        // La matrícula aplica a quien nunca tuvo una membresía en el gym (§4.1); las canceladas cuentan como historial.
        return new ResolvedRenewal(
            plan, start, overrideReason, plan.Kind == RenewalKind.New ? null : previousId,
            history.Count == 0, settings.EnrollmentFee);
    }
}
