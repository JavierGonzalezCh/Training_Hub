using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms.Public;
using GSM.Application.SuperAdmin.Abstractions;
using GSM.Application.SuperAdmin.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using Microsoft.Extensions.Logging;

namespace GSM.Application.SuperAdmin;

/// <summary>Suspensión manual de un gym. El estado <c>suspended</c> solo se llega por aquí, nunca derivado.</summary>
public sealed class SuspendGymHandler(
    IClock clock,
    ICurrentUserService currentUser,
    IGymReader gyms,
    IGymAdministration gymAdministration,
    IGymSubscriptionRepository subscriptions,
    IUnitOfWork unitOfWork,
    ILogger<SuspendGymHandler> logger)
{
    public async Task<SubscriptionDetail> HandleAsync(
        Guid gymId, SuspendGymRequest request, CancellationToken cancellationToken)
    {
        var reason = (request.Reason ?? string.Empty).Trim();
        if (reason.Length is < 1 or > 200)
        {
            throw new ValidationDomainException("El motivo de la suspensión es obligatorio (máx. 200 caracteres).");
        }

        _ = await gyms.GetAdminViewAsync(gymId, cancellationToken)
            ?? throw new NotFoundException("El gym no existe.");

        var sub = await subscriptions.GetByGymIdAsync(gymId, cancellationToken)
            ?? throw new NotFoundException("El gym no tiene suscripción.");

        await gymAdministration.SetCommercialStatusAsync(gymId, GymStatus.Suspended, cancellationToken);

        var note = $"[{clock.Today:O}] Suspendido. Motivo: {reason} (por {currentUser.UserId})";
        sub.SetNotes(string.IsNullOrEmpty(sub.Notes) ? note : $"{sub.Notes}\n{note}");

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Gym {GymId} suspendido. Motivo: {Reason}", gymId, reason);

        return SubscriptionMapper.ToDetail(sub, GymStatus.Suspended, clock.Today);
    }
}
