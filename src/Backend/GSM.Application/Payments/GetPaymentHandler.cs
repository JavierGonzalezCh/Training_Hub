using GSM.Application.Common.Abstractions;
using GSM.Application.Payments.Abstractions;
using GSM.Application.Payments.Contracts;
using GSM.Domain.Common.Exceptions;

namespace GSM.Application.Payments;

public sealed class GetPaymentHandler(ICurrentUserService currentUser, IPaymentRepository payments)
{
    public async Task<PaymentResponse> HandleAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        var payment = await payments.GetByIdAsync(paymentId, cancellationToken)
            ?? throw new NotFoundException("Pago no encontrado.");

        return PaymentMapper.ToResponse(payment) with
        {
            IsReversed = await payments.IsReversedAsync(payment.Id, cancellationToken),
        };
    }
}
