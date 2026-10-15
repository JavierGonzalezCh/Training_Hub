using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Gyms.Events;
using GSM.Application.SuperAdmin.Abstractions;
using GSM.Domain.SuperAdmin;
using Microsoft.Extensions.Logging;

namespace GSM.Application.SuperAdmin.Events;

/// <summary>
/// Al crear un gym se le da de alta su suscripción: de prueba (los días que eligió el SuperAdmin) o, sin
/// prueba, con su primer pago registrado y el gym activo hasta donde ese pago cubre (SuperAdmin §V2). Sin esto, un cliente creado después
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
    ISubscriptionPaymentRepository payments,
    ISaasPlanRepository saasPlans,
    IUnitOfWork unitOfWork,
    ILogger<GymCreatedProvisionsSubscriptionHandler> logger) : IDomainEventHandler<GymCreated>
{
    /// <summary>Prueba por defecto de un evento sin días ni pago (eventos anteriores a G1+S1).</summary>
    public const int TrialDays = 30;

    // Idempotente (un reintento desde logs.event_handler_failures no duplica nada): sin suscripción previa,
    // suscripción y primer pago se guardan juntos en un solo SaveChanges.
    public async Task HandleAsync(GymCreated domainEvent, CancellationToken cancellationToken = default)
    {
        if (await subscriptions.GetByGymIdAsync(domainEvent.GymId, cancellationToken) is not null)
        {
            return;
        }

        var today = clock.Today;

        // El plan del alta (enum de Gyms) se enlaza con su fila de superadmin.saas_plans por nombre (S2).
        var saasPlan = await saasPlans.GetByNameAsync(domainEvent.Plan.ToString(), cancellationToken);

        if (domainEvent.FirstPayment is { } first)
        {
            var createdBy = domainEvent.CreatedBy
                ?? throw new InvalidOperationException("El primer pago necesita el superadmin que dio de alta el gym.");

            var paidSubscription = GymSubscription.Create(
                domainEvent.GymId,
                domainEvent.Plan,
                domainEvent.MonthlyPrice,
                startedOn: today,
                paidThrough: first.CoversThrough,
                trialEndsOn: null,
                notes: null);
            if (saasPlan is not null)
            {
                paidSubscription.AssignSaasPlan(saasPlan.Id);
            }

            subscriptions.Add(paidSubscription);

            // Pago del SaaS: no se mezcla con los pagos del gym.
            payments.Add(SubscriptionPayment.Create(
                domainEvent.GymId,
                first.Amount,
                first.Method,
                first.CoversFrom,
                first.CoversThrough,
                clock.UtcNow,
                createdBy,
                first.Reference,
                first.Notes));

            await unitOfWork.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Suscripción del gym {GymId} creada sin prueba: primer pago {Amount} cubre hasta {CoversThrough}",
                domainEvent.GymId, first.Amount, first.CoversThrough);
            return;
        }

        var trialEndsOn = today.AddDays(domainEvent.TrialDays ?? TrialDays);

        // El precio se negocia con el cliente: si no se indicó en el alta nace en 0 y el superadmin lo fija con el PUT.
        var trialSubscription = GymSubscription.Create(
            domainEvent.GymId,
            domainEvent.Plan,
            monthlyPrice: domainEvent.MonthlyPrice,
            startedOn: today,
            paidThrough: trialEndsOn,
            trialEndsOn: trialEndsOn,
            notes: null);
        if (saasPlan is not null)
        {
            trialSubscription.AssignSaasPlan(saasPlan.Id);
        }

        subscriptions.Add(trialSubscription);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Suscripción de prueba creada para el gym {GymId}: plan {Plan}, prueba hasta {TrialEndsOn}",
            domainEvent.GymId, domainEvent.Plan, trialEndsOn);
    }
}
