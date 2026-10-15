using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms.Public;
using GSM.Application.SuperAdmin.Abstractions;
using GSM.Application.SuperAdmin.Contracts;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.SuperAdmin;

/// <summary>
/// Renovación <b>sin pago</b>: mueve <c>paid_through</c> a una fecha exacta (cortesía o corrección).
/// Deja rastro en las notas de la suscripción; no inventa un <c>SubscriptionPayment</c>.
/// </summary>
public sealed class RenewSubscriptionHandler(
    IClock clock,
    ICurrentUserService currentUser,
    IGymReader gyms,
    IGymSubscriptionRepository subscriptions,
    SubscriptionDetailBuilder detailBuilder,
    IUnitOfWork unitOfWork,
    ILogger<RenewSubscriptionHandler> logger)
{
    public async Task<SubscriptionDetail> HandleAsync(
        Guid gymId, RenewSubscriptionRequest request, CancellationToken cancellationToken)
    {
        var reason = (request.Reason ?? string.Empty).Trim();
        if (reason.Length is < 1 or > 200)
        {
            throw new ValidationDomainException("El motivo de la renovación es obligatorio (máx. 200 caracteres).");
        }

        var gym = await gyms.GetAdminViewAsync(gymId, cancellationToken)
            ?? throw new NotFoundException("El gym no existe.");

        var sub = await subscriptions.GetByGymIdAsync(gymId, cancellationToken)
            ?? throw new NotFoundException("El gym no tiene suscripción.");

        var previous = sub.PaidThrough;
        sub.OverrideCoverage(request.PaidThrough);

        var stamp = clock.Today.ToString("O");
        var note = $"[{stamp}] Renovación sin pago: {previous:O} → {request.PaidThrough:O}. Motivo: {reason} (por {currentUser.UserId})";
        sub.SetNotes(string.IsNullOrEmpty(sub.Notes) ? note : $"{sub.Notes}\n{note}");

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Gym {GymId} renovado sin pago: {Previous} → {New}. Motivo: {Reason}",
            gymId, previous, request.PaidThrough, reason);

        return await detailBuilder.BuildAsync(sub, gym.ManualStatus, cancellationToken);
    }
}
