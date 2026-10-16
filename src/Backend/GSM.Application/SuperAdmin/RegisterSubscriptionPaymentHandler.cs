using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.SuperAdmin.Events;
using GSM.Application.Gyms.Public;
using GSM.Application.SuperAdmin.Abstractions;
using GSM.Application.SuperAdmin.Contracts;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.SuperAdmin;
using Microsoft.Extensions.Logging;

namespace GSM.Application.SuperAdmin;

/// <summary>
/// Operación central del módulo: guarda el pago <b>y</b> empuja <c>paid_through</c> hasta
/// <c>covers_through</c> en la misma transacción. Si el guardado falla, la fecha no se mueve.
/// </summary>
public sealed class RegisterSubscriptionPaymentHandler(
    IClock clock,
    ICurrentUserService currentUser,
    IGymReader gyms,
    IGymSubscriptionRepository subscriptions,
    ISubscriptionPaymentRepository payments,
    SubscriptionDetailBuilder detailBuilder,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<RegisterSubscriptionPaymentHandler> logger)
{
    public async Task<RegisterSubscriptionPaymentResponse> HandleAsync(
        Guid gymId, RegisterSubscriptionPaymentRequest request, CancellationToken cancellationToken)
    {
        var registeredBy = currentUser.UserId
            ?? throw new UnauthorizedDomainException("No hay usuario en el contexto.");

        var gym = await gyms.GetAdminViewAsync(gymId, cancellationToken)
            ?? throw new NotFoundException("El gym no existe.");

        var subscription = await subscriptions.GetByGymIdAsync(gymId, cancellationToken)
            ?? throw new NotFoundException("El gym no tiene suscripción.");

        var (coversFrom, coversThrough) = ResolvePeriod(subscription, request.CoversFrom, request.CoversThrough);

        var payment = SubscriptionPayment.Create(
            gymId,
            request.Amount,
            request.Method,
            coversFrom,
            coversThrough,
            clock.UtcNow,
            registeredBy,
            request.Reference,
            request.Notes);

        payments.Add(payment);
        subscription.ExtendCoverageTo(coversThrough);

        // Un solo SaveChanges: pago y paid_through commitean juntos o no commitea ninguno.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Pago SaaS {PaymentId} del gym {GymId} por {Amount} ({Method}); cobertura hasta {CoversThrough}",
            payment.Id, gymId, payment.Amount, payment.Method, coversThrough);

        // S3: "Recibimos tu pago; tu suscripción va hasta…". Después del commit; la referencia es el pago.
        await events.PublishAsync(
            new SubscriptionNoticeDue(
                gymId, SubscriptionNoticeKind.PaymentRegistered, payment.Id, subscription.PaidThrough, null,
                payment.Amount, null, null),
            cancellationToken);

        return new RegisterSubscriptionPaymentResponse(
            SubscriptionMapper.ToItem(payment),
            await detailBuilder.BuildAsync(subscription, gym.ManualStatus, cancellationToken));
    }

    private static (DateOnly From, DateOnly Through) ResolvePeriod(
        GymSubscription subscription, DateOnly? from, DateOnly? through)
    {
        if (from is null && through is null)
        {
            var start = subscription.NextPeriodStart();
            return (start, start.AddMonths(1).AddDays(-1));
        }

        if (from is null || through is null)
        {
            throw new ValidationDomainException("Indica ambas fechas del periodo cubierto, o ninguna.");
        }

        return (from.Value, through.Value);
    }
}
