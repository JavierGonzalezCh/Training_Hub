using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Payments.Abstractions;
using GSM.Application.Payments.Contracts;
using GSM.Application.Payments.Events;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Payments;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Payments;

/// <summary>
/// Anula un pago registrando su asiento inverso con fecha de hoy. El original no se toca, así que
/// un cierre de caja de un día pasado sigue igual; la anulación cuenta en la caja del día en que se hace.
/// </summary>
public sealed class VoidPaymentHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IPaymentRepository payments,
    IVoidRequestRepository requests,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<VoidPaymentHandler> logger)
{
    public async Task<PaymentResponse> HandleAsync(
        Guid paymentId, VoidPaymentRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var registeredBy = currentUser.UserId
            ?? throw new UnauthorizedDomainException("No hay usuario en el contexto.");

        var original = await payments.GetByIdAsync(paymentId, cancellationToken)
            ?? throw new NotFoundException("Pago no encontrado.");

        if (await payments.IsReversedAsync(original.Id, cancellationToken))
        {
            throw new ConflictException("Este pago ya está anulado.");
        }

        var reversal = Payment.CreateReversal(original, request.Reason, registeredBy, clock.UtcNow);

        payments.Add(reversal);

        // Anular directo cierra la solicitud pendiente de ese pago (si la hay): ya no hay nada que decidir.
        var pending = await requests.GetPendingForPaymentAsync(original.Id, cancellationToken);
        pending?.Approve(registeredBy, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Pago {PaymentId} anulado en gym {GymId} con el asiento {ReversalId} por {Amount}",
            original.Id, gymId, reversal.Id, reversal.Amount);

        await events.PublishAsync(
            new PaymentRecorded(reversal.Id, gymId, reversal.MemberId, reversal.MembershipId, reversal.Amount),
            cancellationToken);

        if (pending is not null)
        {
            await events.PublishAsync(
                new VoidRequestResolved(gymId, pending.Id, original.Id, registeredBy, Approved: true),
                cancellationToken);
        }

        return PaymentMapper.ToResponse(reversal);
    }
}
