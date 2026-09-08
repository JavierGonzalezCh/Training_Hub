using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Members.Public;
using GSM.Application.Memberships.Public;
using GSM.Application.Payments.Abstractions;
using GSM.Application.Payments.Contracts;
using GSM.Application.Payments.Events;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Payments;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Payments;

public sealed class RecordPaymentHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberReader members,
    IMembershipReader memberships,
    IPaymentRepository payments,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<RecordPaymentHandler> logger)
{
    public async Task<PaymentResponse> HandleAsync(
        RecordPaymentRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var registeredBy = currentUser.UserId
            ?? throw new UnauthorizedDomainException("No hay usuario en el contexto.");

        if (request.MemberId is { } memberId)
        {
            if (!await members.ExistsAsync(memberId, cancellationToken))
            {
                throw new NotFoundException("El miembro del pago no existe.");
            }

            if (request.MembershipId is { } membershipId
                && !await memberships.BelongsToMemberAsync(membershipId, memberId, cancellationToken))
            {
                throw new ConflictException("La membresía indicada no pertenece a ese miembro.");
            }
        }

        var payment = Payment.Create(
            gymId,
            request.MemberId,
            request.MembershipId,
            request.Amount,
            request.Method,
            request.Concept,
            request.Notes,
            registeredBy,
            clock.UtcNow);

        payments.Add(payment);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Pago {PaymentId} registrado en gym {GymId} por {Amount} ({Method})",
            payment.Id, gymId, payment.Amount, payment.Method);

        await events.PublishAsync(
            new PaymentRecorded(payment.Id, gymId, payment.MemberId, payment.MembershipId, payment.Amount),
            cancellationToken);

        return PaymentMapper.ToResponse(payment);
    }
}
