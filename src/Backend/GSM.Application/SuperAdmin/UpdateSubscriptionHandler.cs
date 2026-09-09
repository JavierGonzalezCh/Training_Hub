using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms.Public;
using GSM.Application.SuperAdmin.Abstractions;
using GSM.Application.SuperAdmin.Contracts;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.SuperAdmin;

/// <summary>Cambia plan y precio negociado de un cliente. No toca la cobertura.</summary>
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

        var sub = await subscriptions.GetByGymIdAsync(gymId, cancellationToken)
            ?? throw new NotFoundException("El gym no tiene suscripción.");

        sub.UpdatePlan(request.Plan, request.MonthlyPrice);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Suscripción del gym {GymId} actualizada: plan {Plan}, precio {Price}",
            gymId, request.Plan, request.MonthlyPrice);

        return SubscriptionMapper.ToDetail(sub, gym.ManualStatus, clock.Today);
    }
}
