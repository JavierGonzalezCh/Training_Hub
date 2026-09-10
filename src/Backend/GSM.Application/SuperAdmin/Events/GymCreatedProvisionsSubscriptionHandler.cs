using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Gyms.Events;
using GSM.Application.SuperAdmin.Abstractions;
using GSM.Domain.SuperAdmin;
using Microsoft.Extensions.Logging;

namespace GSM.Application.SuperAdmin.Events;

/// <summary>
/// Al crear un gym se le da de alta su suscripción de prueba. Sin esto, un cliente creado después
/// de la migración de la etapa 11.1 se quedaba sin suscripción para siempre: no se le podía poner
/// precio ni cobrarle, y nunca entraba en la cartera.
/// </summary>
/// <remarks>
/// Va por evento y no por escritura cruzada: el alta de gym no necesita la suscripción en su
/// respuesta, así que es reacción y el camino normal basta (<c>CLAUDE.md</c>). Si el handler
/// fallara, el gym queda sin suscripción y —por la decisión explícita de la etapa 11.2— opera con
/// normalidad; el superadmin la repone con <c>PUT /api/admin/gyms/{id}/subscription</c>.
/// </remarks>
public sealed class GymCreatedProvisionsSubscriptionHandler(
    IClock clock,
    IGymSubscriptionRepository subscriptions,
    IUnitOfWork unitOfWork,
    ILogger<GymCreatedProvisionsSubscriptionHandler> logger) : IDomainEventHandler<GymCreated>
{
    /// <summary>Duración de la prueba con la que nace todo gym. Tras ella manda <c>paid_through</c>.</summary>
    public const int TrialDays = 30;

    public async Task HandleAsync(GymCreated domainEvent, CancellationToken cancellationToken = default)
    {
        if (await subscriptions.GetByGymIdAsync(domainEvent.GymId, cancellationToken) is not null)
        {
            return;
        }

        var today = clock.Today;
        var trialEndsOn = today.AddDays(TrialDays);

        // El precio se negocia con el cliente: nace en 0 y el superadmin lo fija con el PUT.
        subscriptions.Add(GymSubscription.Create(
            domainEvent.GymId,
            domainEvent.Plan,
            monthlyPrice: 0m,
            startedOn: today,
            paidThrough: trialEndsOn,
            trialEndsOn: trialEndsOn,
            notes: null));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Suscripción de prueba creada para el gym {GymId}: plan {Plan}, prueba hasta {TrialEndsOn}",
            domainEvent.GymId, domainEvent.Plan, trialEndsOn);
    }
}
