using System.Text.RegularExpressions;
using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms.Public;
using GSM.Application.SuperAdmin.Abstractions;
using GSM.Application.SuperAdmin.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.SuperAdmin;

namespace GSM.Application.SuperAdmin;

/// <summary>
/// Vista de solo lectura de la suscripción del gym del usuario (S3-3). El gym no paga desde el sistema: aquí solo
/// ve en qué estado está y hasta cuándo. Compone el plan, el estado de hoy, el límite de miembros y los últimos
/// pagos; las notas internas de la suscripción nunca salen, salvo el motivo de una suspensión vigente.
/// </summary>
public sealed partial class GetOwnSubscriptionHandler(
    ICurrentUserService currentUser,
    IGymReader gyms,
    IGymSubscriptionRepository subscriptions,
    ISubscriptionPaymentRepository payments,
    SubscriptionDetailBuilder detailBuilder)
{
    public const int RecentPaymentsCount = 12;

    public async Task<OwnSubscriptionResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();

        var gym = await gyms.GetAdminViewAsync(gymId, cancellationToken)
            ?? throw new NotFoundException("El gym no existe.");

        var sub = await subscriptions.GetByGymIdAsync(gymId, cancellationToken)
            ?? throw new NotFoundException("Tu gym aún no tiene una suscripción registrada.");

        var detail = await detailBuilder.BuildAsync(sub, gym.ManualStatus, cancellationToken);
        var history = await payments.GetByGymAsync(gymId, cancellationToken);

        var reason = detail.EffectiveStatus == nameof(EffectiveSubscriptionStatus.Suspended)
            ? ExtractSuspensionReason(sub.Notes)
            : null;

        return new OwnSubscriptionResponse(
            detail.Plan,
            detail.MonthlyPrice,
            detail.EffectiveStatus,
            reason,
            detail.TrialEndsOn,
            detail.PaidThrough,
            detail.DaysUntilDisabled,
            detail.MemberLimit,
            detail.CountedMembers,
            history.Take(RecentPaymentsCount)
                .Select(p => new OwnSubscriptionPayment(
                    p.Id, p.Amount, p.Method.ToString(), p.CoversFrom, p.CoversThrough, p.PaidAt))
                .ToList());
    }

    // SuspendGymHandler anota "[fecha] Suspendido. Motivo: X (por userId)" en las notas; se toma la última.
    private static string? ExtractSuspensionReason(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return null;
        }

        var matches = SuspensionNote().Matches(notes);
        return matches.Count == 0 ? null : matches[^1].Groups["reason"].Value.Trim();
    }

    [GeneratedRegex(@"Suspendido\. Motivo: (?<reason>.*?) \(por [^)]*\)")]
    private static partial Regex SuspensionNote();
}
