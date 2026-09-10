using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms.Public;
using GSM.Application.SuperAdmin.Abstractions;
using GSM.Application.SuperAdmin.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.SuperAdmin;
using Microsoft.Extensions.Logging;

namespace GSM.Application.SuperAdmin;

/// <summary>
/// Cambia plan y precio negociado de un cliente. No toca la cobertura.
/// </summary>
/// <remarks>
/// Si el gym no tiene suscripción la crea, sin cobertura por delante (<c>paid_through</c> = hoy):
/// es la vía de reposición manual para un gym que se quedó sin ella —creado antes de la etapa 11.1
/// o con el handler de alta caído—, y sin ella ese cliente sería incobrable. La cobertura la pone
/// después un pago o una renovación, que es donde vive esa decisión.
/// </remarks>
public sealed class UpdateSubscriptionHandler(
    IClock clock,
    IGymReader gyms,
    IGymSubscriptionRepository subscriptions,
    IUnitOfWork unitOfWork,
    ILogger<UpdateSubscriptionHandler> logger)
{
    public async Task<SubscriptionDetail> HandleAsync(
        Guid gymId, UpdateSubscriptionRequest request, CancellationToken cancellationToken)
    {
        var gym = await gyms.GetAdminViewAsync(gymId, cancellationToken)
            ?? throw new NotFoundException("El gym no existe.");

        var sub = await subscriptions.GetByGymIdAsync(gymId, cancellationToken);

        if (sub is null)
        {
            var today = clock.Today;
            sub = GymSubscription.Create(
                gymId, request.Plan, request.MonthlyPrice, startedOn: today, paidThrough: today);
            subscriptions.Add(sub);
        }
        else
        {
            sub.UpdatePlan(request.Plan, request.MonthlyPrice);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Suscripción del gym {GymId} actualizada: plan {Plan}, precio {Price}",
            gymId, request.Plan, request.MonthlyPrice);

        return SubscriptionMapper.ToDetail(sub, gym.ManualStatus, clock.Today);
    }
}
