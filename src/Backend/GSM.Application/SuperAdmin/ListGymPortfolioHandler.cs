using GSM.Application.Common.Abstractions;
using GSM.Application.SuperAdmin.Abstractions;
using GSM.Application.SuperAdmin.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.SuperAdmin;
using Microsoft.Extensions.Logging;

namespace GSM.Application.SuperAdmin;

/// <summary>Cartera de clientes: todas las suscripciones con su estado efectivo, más el recuento por estado.</summary>
public sealed class ListGymPortfolioHandler(
    IClock clock,
    IGymSubscriptionRepository subscriptions,
    ISaasPlanRepository saasPlans,
    GSM.Application.Memberships.Public.IMembershipReader memberships,
    ILogger<ListGymPortfolioHandler> logger)
{
    public async Task<PortfolioResponse> HandleAsync(PortfolioQuery query, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var rows = await subscriptions.GetPortfolioAsync(cancellationToken);

        EffectiveSubscriptionStatus? filter = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!Enum.TryParse<EffectiveSubscriptionStatus>(query.Status, ignoreCase: true, out var parsed))
            {
                throw new ValidationDomainException($"Estado '{query.Status}' no reconocido.");
            }

            filter = parsed;
        }

        GymType? typeFilter = null;
        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            if (!Enum.TryParse<GymType>(query.Type, ignoreCase: true, out var parsedType))
            {
                throw new ValidationDomainException($"Tipo '{query.Type}' no reconocido.");
            }

            typeFilter = parsedType;
        }

        var planList = await saasPlans.ListAsync(cancellationToken);
        var plansById = planList.ToDictionary(p => p.Id);

        var items = new List<PortfolioItem>(rows.Count);
        var countByStatus = new Dictionary<string, int>();

        foreach (var row in rows)
        {
            var effective = GymSubscription.DeriveStatus(
                row.ManualStatus, today, row.PaidThrough, row.TrialEndsOn, row.TrialGraceDays);

            var key = effective.ToString();
            countByStatus[key] = countByStatus.GetValueOrDefault(key) + 1;

            if ((filter is { } f && effective != f) || (typeFilter is { } t && row.Type != t))
            {
                continue;
            }

            // Mismo criterio que ISaasPlanRepository.GetGymLimitAsync: el plan elegido, o ninguno.
            var plan = row.SaasPlanId is { } planId
                ? plansById.GetValueOrDefault(planId)
                : null;

            items.Add(new PortfolioItem(
                row.GymId,
                row.GymName,
                row.GymSlug,
                effective.ToString(),
                row.ManualStatus.ToString(),
                plan?.Name ?? "Sin plan",
                row.MonthlyPrice,
                row.PaidThrough,
                GymSubscription.DaysUntilDisabled(row.ManualStatus, today, row.PaidThrough, row.TrialEndsOn, row.TrialGraceDays),
                row.MemberLimitOverride ?? plan?.MemberLimit,
                await memberships.GetCountedMemberCountAsync(row.GymId, cancellationToken),
                row.Type.ToString()));
        }

        logger.LogInformation("Cartera SaaS consultada: {Total} suscripciones, filtro '{Filter}'", rows.Count, query.Status);

        return new PortfolioResponse(
            items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            countByStatus);
    }
}
