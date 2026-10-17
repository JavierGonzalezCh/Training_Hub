using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms.Public;
using GSM.Application.SuperAdmin.Abstractions;
using GSM.Application.SuperAdmin.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.SuperAdmin;
using Microsoft.Extensions.Logging;

namespace GSM.Application.SuperAdmin;

/// <summary>
/// Cambia plan, precio negociado y límite propio de miembros de un cliente. No toca la cobertura.
/// </summary>
/// <remarks>
/// Si el gym no tiene suscripción la crea, sin cobertura por delante (<c>paid_through</c> = hoy):
/// es la vía de reposición manual para un gym que se quedó sin ella —creado antes de la etapa 11.1
/// o con el handler de alta caído—, y sin ella ese cliente sería incobrable. La cobertura la pone
/// después un pago o una renovación, que es donde vive esa decisión.
/// <para>
/// El plan del SaaS (S2) se elige con <c>SaasPlanId</c>; si no viene, el plan actual no cambia. Bajar el límite por debajo de los miembros que ya cuentan no expulsa a nadie: solo bloquea
/// las membresías nuevas para quien hoy no cuenta.
/// </para>
/// </remarks>
public sealed class UpdateSubscriptionHandler(
    IClock clock,
    IGymReader gyms,
    IGymSubscriptionRepository subscriptions,
    ISaasPlanRepository plans,
    SubscriptionDetailBuilder detailBuilder,
    IUnitOfWork unitOfWork,
    ILogger<UpdateSubscriptionHandler> logger)
{
    public async Task<SubscriptionDetail> HandleAsync(
        Guid gymId, UpdateSubscriptionRequest request, CancellationToken cancellationToken)
    {
        var gym = await gyms.GetAdminViewAsync(gymId, cancellationToken)
            ?? throw new NotFoundException("El gym no existe.");

        var plan = await ResolvePlanAsync(request, cancellationToken);

        var sub = await subscriptions.GetByGymIdAsync(gymId, cancellationToken);

        if (sub is null)
        {
            var today = clock.Today;
            sub = GymSubscription.Create(
                gymId, request.MonthlyPrice, startedOn: today, paidThrough: today);
            subscriptions.Add(sub);
        }
        else
        {
            sub.UpdatePrice(request.MonthlyPrice);
        }

        if (plan is not null)
        {
            sub.AssignSaasPlan(plan.Id);
        }

        sub.SetMemberLimitOverride(request.MemberLimitOverride);

        if (request.TrialGraceDays is { } graceDays)
        {
            sub.SetTrialGraceDays(graceDays);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Suscripción del gym {GymId} actualizada: plan {Plan}, precio {Price}, override de miembros {Override}",
            gymId, plan?.Name, request.MonthlyPrice, request.MemberLimitOverride);

        return await detailBuilder.BuildAsync(sub, gym.ManualStatus, cancellationToken);
    }

    private async Task<SaasPlan?> ResolvePlanAsync(UpdateSubscriptionRequest request, CancellationToken cancellationToken)
    {
        if (request.SaasPlanId is { } id)
        {
            return await plans.GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException("El plan del SaaS no existe.");
        }

        return null;
    }
}
