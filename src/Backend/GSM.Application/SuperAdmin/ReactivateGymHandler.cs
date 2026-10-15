using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms.Public;
using GSM.Application.SuperAdmin.Abstractions;
using GSM.Application.SuperAdmin.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using Microsoft.Extensions.Logging;

namespace GSM.Application.SuperAdmin;

/// <summary>Saca a un gym de <c>suspended</c>: vuelve a <c>active</c> como estado manual.</summary>
public sealed class ReactivateGymHandler(
    IClock clock,
    ICurrentUserService currentUser,
    IGymReader gyms,
    IGymAdministration gymAdministration,
    IGymSubscriptionRepository subscriptions,
    SubscriptionDetailBuilder detailBuilder,
    IUnitOfWork unitOfWork,
    ILogger<ReactivateGymHandler> logger)
{
    public async Task<SubscriptionDetail> HandleAsync(Guid gymId, CancellationToken cancellationToken)
    {
        var gym = await gyms.GetAdminViewAsync(gymId, cancellationToken)
            ?? throw new NotFoundException("El gym no existe.");

        var sub = await subscriptions.GetByGymIdAsync(gymId, cancellationToken)
            ?? throw new NotFoundException("El gym no tiene suscripción.");

        if (gym.ManualStatus != GymStatus.Suspended)
        {
            throw new ConflictException("El gym no está suspendido.");
        }

        await gymAdministration.SetCommercialStatusAsync(gymId, GymStatus.Active, cancellationToken);

        var note = $"[{clock.Today:O}] Reactivado (por {currentUser.UserId})";
        sub.SetNotes(string.IsNullOrEmpty(sub.Notes) ? note : $"{sub.Notes}\n{note}");

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Gym {GymId} reactivado", gymId);

        return await detailBuilder.BuildAsync(sub, GymStatus.Active, cancellationToken);
    }
}
